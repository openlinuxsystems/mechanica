"""Faction marks, set B: the bot factions' own marks in the world (docs/world-generation.md, Faction sites).

The colony AI built the world's machines; it fell and its bots split into factions. Each faction now builds from what
it scavenges, so a player can tell whose ground they stand on from a distance. These are two fifth design languages
beside the four of docs/art-direction.md, obeying its rules (silhouette first, framed construction, small bevels, round
only where real, show the hardware, stylised not cartoony, real scale).

Spoolers (the tape bots, tools/spooler): everything is wound, wrapped and taped. Flanged spools hung off gunmetal
racking, the bots' blue-and-white chevron tape, yellow light tape, red heavy tape with its yellow-and-black print,
bubble wrap and kraft cardboard; a camera-lens eye under a hood watches from every lookout. A reel tower (the lair),
a tape web strung between posts, a taped crate bunker, a sniper gantry on pallet racking, a bubble-wrap cocoon, a
cardboard sentinel and a reel-drum barricade.

Threshers (the farm-machine bosses, tools/thresher): industrial agriculture gone feral. Torn-off harvester headers,
lugged tractor tyres, spray booms and pesticide tanks in the three machines' paints (Harvester grey and mustard,
Tiller rust red and cream, Duster olive and hazard yellow), yellow-and-black hazard edges, teeth and tines pointing
out, chartreuse pesticide leaking and tilled scars in the ground. A den in a ring of headers round a combine hulk
with its auger raised as a mast (the lair), a pesticide shrine, a spray mast, a tractor graveyard, a warning stake, a
tyre cairn and a tilled scar.

Each builder returns an empty with the piece under it, its origin on the ground at its centre (Blender Z up, front
towards -Y). They are world objects, built by build_structures.py props. Most carry no collider: the object's data gives
one. The reel tower, the crate bunker and the den carry kit.collider_box shapes, exported as their ``collider`` mesh.
In the game the printed tapes and bubble wrap take their print from textures.py (the chevron, hazard_tape and bubble
finishes); the pesticide mist is a faint glowing surface.
"""
import math
import random

import bpy
from mathutils import Vector

import kit as K
import frame as FR
from families import M, GRAPHITE, RUST, ZINC, CONCRETE
from props import _root, wood, plank
from props_colony_a import stencil, _empty
from props_colony_b import beam, rod, plate_xz, tyre
from props_industry import ladder, hazard_diamond, lump, drum, hazard

# ---------------------------------------------------------------- Spooler palette (tools/spooler/build_spooler.py)
SP_BLUE = "#3e7fd0"        # Blue Spooler paint
SP_NAVY = "#1d3f9a"        # chevron print on white tape
TAPE_WHITE = "#f2f4f6"
BUBBLE = "#8fd0f0"         # bubble wrap
SP_YELLOW = "#e0c030"      # yellow light tape
SP_RED = "#d0402e"         # red heavy tape
HAZ_Y, HAZ_K = "#f0c020", "#141416"   # the heavy tape's print
KRAFT, KRAFT_D = "#b08a58", "#8f6d42"  # cardboard cores and cartons
RACK = "#2e3136"           # gunmetal racking
BARE = "#8d9096"           # bare steel
EYE = "#3ce060"            # the bots' calm eye

# ---------------------------------------------------------------- Thresher palette (tools/thresher/build_thresher.py)
TH_GREY, TH_MUSTARD = "#7a7f86", "#d9a520"      # Harvester
TILL_RED, TILL_CREAM = "#a8402a", "#e2d6b8"     # Tiller
OLIVE, HAZ_YEL, HAZ_BLK = "#6b7a2e", "#e8b820", "#191a1c"   # Duster
TANK = "#c9c7bc"           # chemical tanks
PEST = "#a6d84a"           # pesticide: the mist glow and fresh stain
SPILL = "#2a4424"          # old pesticide soaked into the ground
SOIL, SOIL_D = "#5e4a36", "#463626"
TYRE_C = "#26282a"


# ---------------------------------------------------------------- materials
def _shade(hexs, f):
    h = hexs.lstrip("#")
    return "#" + "".join(f"{max(0, min(255, int(int(h[i:i + 2], 16) * f))):02x}" for i in (0, 2, 4))


def _tape(col):
    """Plain plastic tape: glossy, a little dirty."""
    return K.mat(f"tape{col}", col, rough=0.32, wear=0.05, grime=0.25, scratches=0.05, ao=0.4)


def _wound(col):
    """A roll's side: the wound layers, darker than the face."""
    return K.mat(f"wound{col}", _shade(col, 0.72), rough=0.4, grime=0.25, noise_scale=30.0, ao=0.4)


def _printed(name, a, b, across="Z", period=0.32, kind="chevron"):
    """Printed tape (review renders): chevrons or diagonal stripes of b on a, in the mesh's own coordinates. `across`
    is the axis across the tape's width; the print runs along the other two. Game export keeps the flat colour a."""
    m = K.mat(name, a, rough=0.32, wear=0.05, grime=0.25, scratches=0.05, ao=0.4)
    if K.EXPORT or m.get("printed"):
        return m
    m["printed"] = 1
    n, L = m.node_tree.nodes, m.node_tree.links
    rgb = next(x for x in n if x.type == "RGB")
    tc = n.new("ShaderNodeTexCoord")
    sp = n.new("ShaderNodeSeparateXYZ")
    L.new(tc.outputs["Object"], sp.inputs[0])
    ax = {"X": 0, "Y": 1, "Z": 2}[across]
    others = [i for i in range(3) if i != ax]
    along = n.new("ShaderNodeMath")
    along.operation = "ADD"
    L.new(sp.outputs[others[0]], along.inputs[0])
    L.new(sp.outputs[others[1]], along.inputs[1])
    acr = n.new("ShaderNodeMath")
    acr.operation = "ABSOLUTE" if kind == "chevron" else "MULTIPLY"
    L.new(sp.outputs[ax], acr.inputs[0])
    acr.inputs[1].default_value = 1.0
    s = n.new("ShaderNodeMath")
    s.operation = "ADD"
    L.new(along.outputs[0], s.inputs[0])
    L.new(acr.outputs[0], s.inputs[1])
    w = n.new("ShaderNodeMath")
    w.operation = "WRAP"
    w.inputs[1].default_value = 0.0
    w.inputs[2].default_value = period
    L.new(s.outputs[0], w.inputs[0])
    st = n.new("ShaderNodeMath")
    st.operation = "LESS_THAN"
    st.inputs[1].default_value = period * 0.45
    L.new(w.outputs[0], st.inputs[0])
    mix = n.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    L.new(st.outputs[0], mix.inputs["Factor"])
    mix.inputs[6].default_value = K.lin(a)
    mix.inputs[7].default_value = K.lin(b)
    for lk in list(rgb.outputs[0].links):
        L.new(mix.outputs[2], lk.to_socket)
    return m


def _chevron(across="Z"):
    """The Blue Spooler's tape: navy chevrons on white."""
    return _printed(f"chev{across}", TAPE_WHITE, SP_NAVY, across, period=0.24)


def _hazard_tape(across="Z"):
    """The Red Spooler's heavy tape: yellow and black diagonals."""
    return _printed(f"haztape{across}", HAZ_Y, HAZ_K, across, period=0.28, kind="diag")


def _bubble():
    """Bubble wrap: pale blue and glossy, its bubbles a bump in review renders."""
    m = K.mat("bubble", BUBBLE, rough=0.1, grime=0.12, ao=0.3, noise_scale=3.0)
    if K.EXPORT or m.get("bubbled"):
        return m
    m["bubbled"] = 1
    n, L = m.node_tree.nodes, m.node_tree.links
    tc = n.new("ShaderNodeTexCoord")
    vo = n.new("ShaderNodeTexVoronoi")
    vo.inputs["Scale"].default_value = 11.0
    vo.inputs["Randomness"].default_value = 0.0
    L.new(tc.outputs["Object"], vo.inputs["Vector"])
    mr = n.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = 0.0
    mr.inputs["From Max"].default_value = 0.42
    mr.inputs["To Min"].default_value = 1.0
    mr.inputs["To Max"].default_value = 0.0
    L.new(vo.outputs["Distance"], mr.inputs["Value"])
    bp = n.new("ShaderNodeBump")
    bp.inputs["Strength"].default_value = 0.8
    bp.inputs["Distance"].default_value = 0.02
    L.new(mr.outputs[0], bp.inputs["Height"])
    b = n["Principled BSDF"]
    L.new(bp.outputs["Normal"], b.inputs["Normal"])
    b.inputs["Coat Weight"].default_value = 0.5
    return m


def _kraft(dark=False):
    return K.mat("kraft_d" if dark else "kraft", KRAFT_D if dark else KRAFT, rough=0.92, grime=0.45, wear=0.05, noise_scale=9.0)


def _rack():
    return M(RACK, metal=0.5, wear=0.25, grime=0.4)


def _bare():
    return M(BARE, metal=0.7, wear=0.15, grime=0.4, rough=0.4)


def _paint(col, **kw):
    """The bots' battle-worn paint, chipped through at the edges."""
    kw.setdefault("wear", 0.25)
    kw.setdefault("grime", 0.45)
    kw.setdefault("scratches", 0.45)
    return M(col, **kw)


def _dark():
    return M(GRAPHITE, metal=0.4, wear=0.15)


def _zinc():
    return M(ZINC, metal=0.6, wear=0.2)


def _eye():
    return K.mat("bot_eye", "#1d7a30", rough=0.15, emit=EYE, emit_strength=5.0)


def _pest(strength=2.5):
    return K.mat(f"pest{strength}", PEST, rough=0.6, emit=PEST, emit_strength=strength)


def _mist_mat():
    """Pesticide mist: a soft glowing volume in review renders; the game gets a faint emissive surface."""
    if K.EXPORT:
        return _pest(1.2)
    m = bpy.data.materials.get("pest_mist")
    if m:
        return m
    m = bpy.data.materials.new("pest_mist")
    m.use_nodes = True
    n, L = m.node_tree.nodes, m.node_tree.links
    out = n["Material Output"]
    n.remove(n["Principled BSDF"])
    v = n.new("ShaderNodeVolumePrincipled")
    v.inputs["Color"].default_value = K.lin(PEST)
    v.inputs["Density"].default_value = 1.6
    v.inputs["Emission Color"].default_value = K.lin(PEST)
    v.inputs["Emission Strength"].default_value = 0.6
    L.new(v.outputs[0], out.inputs["Volume"])
    return m


def _soil(dark=False):
    return K.mat("soil_d" if dark else "soil", SOIL_D if dark else SOIL, rough=1.0, grime=0.1, noise_scale=14.0, cracks=0.35, ao=0.5)


def _tyre_mat():
    return K.mat("tyre", TYRE_C, rough=0.9, grime=0.4)


# ---------------------------------------------------------------- shared shapes
def ribbon(P, name, a, b, w, mat, sag=0.0, t=0.006, n=3):
    """A strip of tape stretched from a to b, sagging in the middle; its width faces sideways (local X across)."""
    a, b = Vector(a), Vector(b)
    pts = []
    for i in range(n + 1):
        u = i / n
        p = a.lerp(b, u)
        p.z -= sag * 4 * u * (1 - u)
        pts.append(p)
    for i in range(n):
        beam(P, f"{name}{i}", pts[i], pts[i + 1], w, mat, d=t, bev=0)


def band_box(P, name, center, size, z, w, mat, rot=(0, 0, 0)):
    """A band of tape round a box (center, size): a skin a little larger than the box, w wide at height z."""
    cx, cy, cz = center
    sx, sy, _ = size
    return K.box(name, (sx + 0.016, sy + 0.016, w), mat, loc=(cx, cy, z), rot=rot, bev=0, parent=P)


def eye_lamp(P, loc, r, rot=(0, 0, 0), lit=True, lite=False):
    """The Spoolers' watching eye: a camera lens under a hood, looking along local -Y."""
    G = _empty(P, "eye", loc, rot)
    sg, bv = (14, 0) if lite else (20, 0.01)
    K.box("eye_hood", (r * 2.6, r * 1.3, r * 0.22), _rack(), loc=(0, -r * 0.35, r * 1.18), bev=bv, parent=G)
    K.cyl("eye_barrel", r * 1.12, r * 0.9, _dark(), loc=(0, -r * 0.2, 0), rot=(90, 0, 0), segs=sg, bev=bv, parent=G)
    K.cyl("eye_bezel", r * 0.95, r * 0.12, M("#141416", metal=0.5, wear=0.1), loc=(0, -r * 0.68, 0), rot=(90, 0, 0), segs=sg, bev=bv / 2, parent=G)
    lens = _eye() if lit else K.mat("deadlens", "#2c3a34", rough=0.12, metal=0.2)
    K.cyl("eye_lens", r * 0.62, r * 0.1, lens, loc=(0, -r * 0.72, 0), rot=(90, 0, 0), segs=16, bev=0, parent=G)
    return G


def spool(P, name, loc, R, w, tape, axis="x", core=0.32, flange=None, ribs=6, lite=False):
    """A flanged spool wound with tape: gunmetal flanges with ribs and bolts, a kraft core, the wound tape's side.
    lite (the reel tower's giant spools): fewer sides, no bevels and no bolts."""
    rot = {"x": (0, 90, 0), "y": (90, 0, 0), "z": (0, 0, 0)}[axis]
    G = _empty(P, name, loc, rot)                      # local Z is the spool's axis
    fl = flange or _rack()
    sg = 20 if lite else 28
    K.cyl(f"{name}_tape", R * 0.9, w, tape, segs=sg, bev=0 if lite else 0.01, parent=G)
    for s in (-1, 1):
        K.cyl(f"{name}_flange", R, 0.07, fl, loc=(0, 0, s * (w / 2 + 0.035)), segs=sg, bev=0 if lite else 0.012, parent=G)
        K.cyl(f"{name}_boss", R * core, 0.12, _kraft(), loc=(0, 0, s * (w / 2 + 0.12)), segs=10 if lite else 16, bev=0 if lite else 0.01, parent=G)
        K.cyl(f"{name}_hole", R * core * 0.55, 0.13, _dark(), loc=(0, 0, s * (w / 2 + 0.125)), segs=8 if lite else 12, bev=0, parent=G)
        for k in range(ribs):
            a = 2 * math.pi * (k + 0.5) / ribs
            rr = (R * core + R) / 2
            K.box(f"{name}_rib", (R * (1 - core) * 0.9, 0.05, 0.05), fl,
                  loc=(math.cos(a) * rr, math.sin(a) * rr, s * (w / 2 + 0.09)), rot=(0, 0, math.degrees(a)), bev=0, parent=G)
            if not lite:
                K.cyl(f"{name}_bolt", 0.035, 0.04, _zinc(), loc=(math.cos(a) * R * 0.9, math.sin(a) * R * 0.9, s * (w / 2 + 0.08)),
                      segs=6, bev=0, parent=G)
    return G


def tape_roll(P, name, loc, R, w, tape, side_col, axis="x", core=0.42, lite=False):
    """A plain roll of tape on a cardboard core (no flanges), like the rolls the bots carry."""
    rot = {"x": (0, 90, 0), "y": (90, 0, 0), "z": (0, 0, 0)}[axis]
    G = _empty(P, name, loc, rot)
    sg = 16 if lite else 24
    K.cyl(f"{name}_face", R, w, tape, segs=sg, bev=0 if lite else 0.006, parent=G)
    K.cyl(f"{name}_side", R * 0.96, w + 0.006, _wound(side_col), segs=sg, bev=0, parent=G)
    K.cyl(f"{name}_core", R * core, w + 0.012, _kraft(), segs=10 if lite else 16, bev=0, parent=G)
    K.cyl(f"{name}_hole", R * core * 0.82, w + 0.016, _dark(), segs=10 if lite else 16, bev=0, parent=G)
    return G


def carton(P, name, loc, size, yaw=0.0, rot=None, tape=None, seed=0):
    """A kraft carton, its origin on its underside: flap seam and a strip of tape over it, a band round it."""
    rnd = random.Random(seed)
    sx, sy, sz = size
    G = _empty(P, name, loc, rot or (0, 0, yaw))
    K.box(f"{name}_box", size, _kraft(rnd.random() < 0.35), loc=(0, 0, sz / 2), bev=0.012, parent=G)
    K.box(f"{name}_seam", (sx - 0.04, 0.012, 0.004), _kraft(True), loc=(0, 0, sz + 0.001), bev=0, parent=G)
    t = tape or _tape(SP_YELLOW)
    K.box(f"{name}_strip", (0.07, sy + 0.012, 0.006), t, loc=(0, 0, sz), bev=0, parent=G)
    K.box(f"{name}_flapf", (0.07, 0.006, sz * 0.3), t, loc=(0, -sy / 2 - 0.003, sz * 0.85), bev=0, parent=G)
    K.box(f"{name}_flapb", (0.07, 0.006, sz * 0.3), t, loc=(0, sy / 2 + 0.003, sz * 0.85), bev=0, parent=G)
    return G


def crate(P, name, loc, size, yaw=0.0, shade=0):
    """A warehouse crate: plank faces with corner battens, its origin on its underside."""
    sx, sy, sz = size
    G = _empty(P, name, loc, (0, 0, yaw))
    K.box(f"{name}_body", (sx - 0.02, sy - 0.02, sz - 0.02), wood(shade, wear=0.25), loc=(0, 0, sz / 2), bev=0.01, parent=G)
    for a in (-1, 1):
        for b in (-1, 1):
            K.box(f"{name}_post", (0.08, 0.08, sz), wood(3, wear=0.25), loc=(a * (sx / 2 - 0.04), b * (sy / 2 - 0.04), sz / 2), bev=0, parent=G)
    for z in (0.05, sz - 0.05):
        K.box(f"{name}_rim", (sx + 0.005, sy + 0.005, 0.09), wood((shade + 2) % 4, wear=0.25), loc=(0, 0, z), bev=0, parent=G)
    return G


def pallet_flat(P, name, loc, yaw=0.0):
    """A cheap pallet: three bearers and a deck of five boards (1.2 x 1.0 m, 0.14 m tall)."""
    G = _empty(P, name, loc, (0, 0, yaw))
    for k, x in enumerate((-0.53, 0.0, 0.53)):
        K.box(f"{name}_bearer{k}", (0.12, 1.0, 0.1), wood(3), loc=(x, 0, 0.05), bev=0, parent=G)
    for k in range(5):
        K.box(f"{name}_deck{k}", (1.2, 0.14, 0.025), wood(k), loc=(0, -0.43 + k * 0.215, 0.115), bev=0, parent=G)
    return G


def ground_pad(P, name, w, d, mat=None, h=0.12):
    """A low slab the piece stands on, sunk a little so it meets the ground."""
    return K.box(name, (w, d, h), mat or K.mat("concrete", CONCRETE, rough=0.9, wear=0.3, grime=0.6, cracks=0.2, moss=0.2),
                 loc=(0, 0, h / 2 - 0.04), bev=0.03, parent=P)


def stake(P, name, x, y, h=0.9, lean=(0, 0), mat=None):
    """A short steel ground stake (a tie-down for tape lines)."""
    return K.box(name, (0.05, 0.05, h), mat or _rack(), loc=(x, y, h / 2 - 0.25), rot=(lean[0], lean[1], 0), bev=0, parent=P)


# ================================================================== Spoolers
def spooler_reel_tower(seed=101):
    """The Spoolers' lair: a 19 m tower of gunmetal racking with three giant tape spools hung off its sides, a lookout
    head with the bots' camera eye on top and tape lines guyed out to stakes like a web."""
    rnd = random.Random(seed)
    P = _root("reel_tower")
    rk = _rack()
    ground_pad(P, "pad", 5.2, 5.2)
    Z0, Z1, B, T = 0.08, 14.0, 2.0, 1.25          # leg foot and top heights, base and top half-widths

    def hw(z):
        return B + (T - B) * (z - Z0) / (Z1 - Z0)

    for sx in (-1, 1):
        for sy in (-1, 1):
            beam(P, "leg", (sx * B, sy * B, Z0), (sx * T, sy * T, Z1), 0.3, rk, d=0.24, bev=0)
            K.collider_only(beam(P, "col_leg", (sx * B, sy * B, Z0), (sx * T, sy * T, Z1), 0.3, None, d=0.3, bev=0))
            for k, z in enumerate((1.6, 7.0, 12.4)):
                h = hw(z)
                bz = K.box("leg_tape", (0.34, 0.28, 0.32), _chevron("Z") if k != 1 else _tape(SP_YELLOW), loc=(sx * h, sy * h, z), bev=0, parent=P)
                bz.rotation_euler = (math.radians(-sy * 3), math.radians(sx * 3), 0)
            K.box("foot", (0.5, 0.5, 0.08), rk, loc=(sx * B, sy * B, 0.12), bev=0, parent=P)
    levels = [Z0 + 0.4, 3.0, 5.8, 8.6, 11.3, Z1]
    for i, z in enumerate(levels):
        h = hw(z)
        for s in (-1, 1):
            beam(P, "tie", (-h, s * h, z), (h, s * h, z), 0.12, rk, bev=0)
            beam(P, "tie", (s * h, -h, z), (s * h, h, z), 0.12, rk, bev=0)
        if i + 1 < len(levels):
            z2 = levels[i + 1]
            h2 = hw(z2)
            f = 1 if i % 2 else -1
            for s in (-1, 1):
                beam(P, "brace", (-h * f, s * h, z), (h2 * f, s * h2, z2), 0.08, rk, bev=0)
                beam(P, "brace", (s * h, h * f, z), (s * h2, -h2 * f, z2), 0.08, rk, bev=0)
    # three spools on cantilevered axles: blue chevron low on the left, yellow light tape right, red heavy tape front
    for (name, side, z, R, w, tape) in (("spool_a", "-x", 4.4, 2.6, 1.6, _chevron("Z")),
                                         ("spool_b", "+x", 8.6, 2.15, 1.3, _tape(SP_YELLOW)),
                                         ("spool_c", "-y", 11.4, 1.6, 1.1, _hazard_tape("Z"))):
        h = hw(z)
        off = h + 0.25 + w / 2 + 0.2
        if side == "-x":
            loc, axis, a0, a1 = (-off, 0, z), "x", (-h, 0, z), (-off - w / 2 - 0.3, 0, z)
        elif side == "+x":
            loc, axis, a0, a1 = (off, 0, z), "x", (h, 0, z), (off + w / 2 + 0.3, 0, z)
        else:
            loc, axis, a0, a1 = (0, -off, z), "y", (0, -h, z), (0, -off - w / 2 - 0.3, z)
        spool(P, name, loc, R, w, tape, axis=axis, flange=_paint(SP_BLUE, metal=0.3), ribs=4, lite=True)
        rod(P, f"{name}_axle", a0, a1, 0.11, _bare(), segs=10)
        K.collider_only(K.cyl(f"col_{name}", R, w + 0.3, None, loc=loc, rot=(0, 90, 0) if axis == "x" else (90, 0, 0), segs=12, bev=0, parent=P))
        # the bracket that carries the axle: a gusset from the leg tie below
        for s in (-1, 1):
            if axis == "x":
                beam(P, "gusset", (a0[0], s * h * 0.9, z - 1.4), (a0[0] + (0.6 if side == "+x" else -0.6), 0, z - 0.05), 0.1, rk, bev=0)
            else:
                beam(P, "gusset", (s * h * 0.9, a0[1], z - 1.4), (0, a0[1] - 0.6, z - 0.05), 0.1, rk, bev=0)
    # the loose ends of the tape run off each spool down to stakes in the ground
    lines = [((-hw(4.4) - 1.2, -0.5, 4.4 - 2.3), (-8.0, -4.0, 0.35), _chevron("X")),
             ((-hw(4.4) - 1.4, 0.6, 4.4 - 2.3), (-7.0, 5.0, 0.35), _chevron("X")),
             ((hw(8.6) + 1.1, -0.5, 8.6 - 1.9), (8.5, -4.5, 0.35), _tape(SP_YELLOW)),
             ((hw(8.6) + 1.2, 0.5, 8.6 - 1.9), (7.5, 5.5, 0.35), _tape(SP_YELLOW)),
             ((0.4, -hw(11.4) - 1.2, 11.4 - 1.45), (3.0, -9.5, 0.35), _hazard_tape("X")),
             ((-1.6, -1.7, Z1), (-4.5, -8.0, 0.35), _tape(SP_RED))]
    for k, (a, b, m) in enumerate(lines):
        ribbon(P, f"line{k}", a, b, 0.22, m, sag=0.35, n=2)
        stake(P, f"stake{k}", b[0], b[1], lean=(rnd.uniform(-12, 12), rnd.uniform(-12, 12)))
    # deck and lookout head: the bots' boxy head at building size, eye under its hood, a side reel and an antenna
    K.box("deck", (3.4, 3.4, 0.16), rk, loc=(0, 0, Z1 + 0.08), bev=0.02, parent=P)
    K.collider_box(P, "deck", (3.4, 3.4, 0.16), (0, 0, Z1 + 0.08))
    K.collider_box(P, "head", (3.4, 3.2, 3.3), (0, 0.27, Z1 + 1.8))
    K.collider_box(P, "pad", (5.2, 5.2, 0.12), (0, 0, 0.02))
    for k, (x, y) in enumerate(((1.9, -2.9), (-2.8, -2.4))):
        K.collider_box(P, f"crates{k}", (1.2, 1.0, 1.4 if k == 0 else 0.6), (x, y, 0.7 if k == 0 else 0.3))
    for s in (-1, 1):
        K.box("rail_x", (3.4, 0.06, 0.06), rk, loc=(0, s * 1.67, Z1 + 1.1), bev=0, parent=P)
        K.box("rail_y", (0.06, 3.4, 0.06), rk, loc=(s * 1.67, 0, Z1 + 1.1), bev=0, parent=P)
        for t in (-1, 1):
            K.box("rail_post", (0.07, 0.07, 1.0), rk, loc=(s * 1.67, t * 1.67, Z1 + 0.62), bev=0, parent=P)
    for k in range(10):
        side = k % 4
        u = -1.4 + (k // 4) * 1.3 + rnd.uniform(-0.1, 0.1)
        L = rnd.uniform(1.2, 3.2)
        m = [_chevron("X"), _tape(SP_YELLOW), _tape(SP_RED), _chevron("X")][k % 4]
        if side in (0, 1):
            y = (-1.72 if side == 0 else 1.72)
            K.box("streamer", (0.16, 0.006, L), m, loc=(u, y, Z1 - L / 2), rot=(rnd.uniform(-5, 5), 0, 0), bev=0, parent=P)
        else:
            x = (-1.72 if side == 2 else 1.72)
            K.box("streamer", (0.006, 0.16, L), m, loc=(x, u, Z1 - L / 2), rot=(0, rnd.uniform(-5, 5), 0), bev=0, parent=P)
    hz = Z1 + 0.16
    H = 2.3
    # the head is the bots' head grown about its foot so it reads from afar
    Hd = _empty(P, "head_grp", (0, 0, hz * (1 - 1.35)))
    Hd.scale = (1.35, 1.35, 1.35)
    K.box("head", (2.3, 2.1, H), _paint(SP_BLUE), loc=(0, 0.2, hz + H / 2), bev=0.05, parent=Hd)
    for s in (-1, 1):
        for t in (-1, 1):
            K.box("head_post", (0.14, 0.14, H + 0.04), rk, loc=(s * 1.12, 0.2 + t * 1.02, hz + H / 2), bev=0, parent=Hd)
    K.box("head_cap", (2.5, 2.3, 0.14), rk, loc=(0, 0.2, hz + H + 0.07), bev=0.02, parent=Hd)
    # patches of tape and kraft over its battle damage
    K.box("patch1", (0.7, 0.012, 0.5), _kraft(), loc=(-0.65, -0.856, hz + 1.85), rot=(0, 9, 0), bev=0, parent=Hd)
    K.box("patch2", (0.9, 0.012, 0.12), _tape(SP_YELLOW), loc=(-0.6, -0.864, hz + 1.85), rot=(0, -24, 0), bev=0, parent=Hd)
    K.box("patch3", (0.012, 0.8, 0.6), _kraft(True), loc=(1.156, 0.5, hz + 0.8), rot=(6, 0, 0), bev=0, parent=Hd)
    eye_lamp(Hd, (0.25, -0.86, hz + 1.25), 0.42, lite=True)
    for k in range(5):
        K.box("grille", (1.0, 0.06, 0.05), _dark(), loc=(0.25, -0.88, hz + 0.25 + k * 0.1), bev=0, parent=Hd)
    tape_roll(Hd, "head_reel", (-1.55, 0.2, hz + 1.3), 0.75, 0.42, _chevron("Z"), TAPE_WHITE, axis="x", lite=True)
    K.torus("reel_guard", 0.86, 0.035, rk, loc=(-1.55, 0.2, hz + 1.3), rot=(0, 90, 0), arc=200, segs=20, parent=Hd)
    K.cyl("ear", 0.32, 0.12, _bare(), loc=(1.21, 0.0, hz + 1.3), rot=(0, 90, 0), segs=10, bev=0, parent=Hd)
    K.box("ear_slot", (0.03, 0.42, 0.06), _dark(), loc=(1.28, 0.0, hz + 1.3), rot=(35, 0, 0), bev=0, parent=Hd)
    rod(Hd, "antenna", (0.7, 0.8, hz + H + 0.1), (0.7, 0.8, hz + H + 3.0), 0.04, _bare())
    K.box("pennant", (0.012, 0.7, 0.32), _chevron("X"), loc=(0.7, 1.15, hz + H + 2.75), rot=(0, 0, 8), bev=0, parent=Hd)
    stencil(Hd, "SP-01", (0.0, -0.865, hz + 2.05), 0.22, M(TAPE_WHITE, wear=0.2))
    # the ladder up the back: two rails and rungs (no brackets; it is bolted to the ties)
    for s in (-1, 1):
        K.box("lad_rail", (0.06, 0.05, Z1 + 0.2), _zinc(), loc=(s * 0.22, B + 0.25, (Z1 + 0.2) / 2), bev=0, parent=P)
    for k in range(1, int(Z1 / 0.5) + 1):
        K.box("rung", (0.44, 0.03, 0.03), _zinc(), loc=(0, B + 0.25, k * 0.5), bev=0, parent=P)
    # cartons and taped crates stacked at its foot, a spare roll
    crate(P, "crate1", (1.9, -2.9, 0), (1.2, 1.0, 1.0), yaw=8, shade=1)
    carton(P, "carton1", (1.9, -2.9, 1.0), (0.8, 0.6, 0.55), yaw=-12, seed=1)
    carton(P, "carton2", (-2.8, -2.4, 0), (0.7, 0.6, 0.6), yaw=20, seed=2, tape=_chevron("Y"))
    tape_roll(P, "spare", (2.9, 1.6, 0.45), 0.45, 0.5, _tape(SP_RED), SP_RED, axis="y", lite=True)
    return P


def spooler_tape_web(seed=102):
    """A tape web: two racking uprights set in taped drums, 6 m apart, strung with criss-crossed tape that sags and
    catches what blows into it; small tape rolls on the post tops feed it."""
    rnd = random.Random(seed)
    P = _root("tape_web")
    rk = _rack()
    X, Ht = 3.0, 3.6
    for s in (-1, 1):
        drum(P, f"drum{s}", (s * X, 0, 0.44), col=SP_BLUE, wear=0.3)
        K.cyl(f"drum_tape{s}", 0.3, 0.14, _chevron("Z"), loc=(s * X, 0, 0.62), segs=18, bev=0, parent=P)
        beam(P, f"post{s}", (s * X, 0, 0.3), (s * X + s * 0.15, 0, Ht), 0.14, rk, d=0.1, bev=0.015)
        K.box(f"cap{s}", (0.4, 0.3, 0.08), rk, loc=(s * X + s * 0.15, 0, Ht + 0.04), bev=0.01, parent=P)
        tape_roll(P, f"roll{s}", (s * X + s * 0.15, 0, Ht + 0.32), 0.24, 0.16, _tape(SP_YELLOW if s < 0 else SP_RED), SP_YELLOW if s < 0 else SP_RED, axis="y")
        rod(P, f"spindle{s}", (s * X + s * 0.15, -0.12, Ht + 0.32), (s * X + s * 0.15, 0.12, Ht + 0.32), 0.03, _bare())
        # guy tape to a stake behind and in front
        for t in (-1, 1):
            ribbon(P, f"guy{s}{t}", (s * X + s * 0.1, 0, Ht - 0.4), (s * (X + 1.2), t * 1.4, 0.3), 0.09, _tape(SP_YELLOW), sag=0.08, n=2)
            stake(P, f"stake{s}{t}", s * (X + 1.2), t * 1.4, lean=(t * 15, s * 10))
    mats = [_chevron("X"), _tape(SP_YELLOW), _hazard_tape("X"), _chevron("X"), _tape(SP_RED)]
    k = 0
    for z0, z1 in ((0.7, 3.2), (3.3, 0.9), (1.4, 2.5), (2.6, 1.5), (1.0, 1.1), (3.0, 3.0), (2.0, 0.5), (0.6, 2.9)):
        dy = rnd.uniform(-0.04, 0.04)
        ribbon(P, f"strip{k}", (-X + 0.06, dy, z0), (X - 0.06, -dy, z1), 0.12, mats[k % len(mats)], sag=rnd.uniform(0.12, 0.3), n=4)
        k += 1
    # things the web caught: a sheet of kraft, a bubble-wrap scrap, a can
    K.box("caught_card", (0.6, 0.02, 0.45), _kraft(), loc=(-0.9, 0.04, 2.05), rot=(0, 18, 4), bev=0.004, parent=P)
    K.box("caught_bubble", (0.5, 0.03, 0.4), _bubble(), loc=(1.3, -0.04, 1.2), rot=(0, -25, 0), bev=0.01, parent=P)
    K.cyl("caught_can", 0.06, 0.16, _zinc(), loc=(0.4, 0.03, 1.55), rot=(80, 0, 30), segs=10, bev=0.004, parent=P)
    return P


def spooler_crate_bunker(seed=103):
    """A taped crate bunker: a U of crates and cartons two high, every one banded in tape, bubble-wrap rolls along the
    top as sandbags, a bubble-wrap blanket over the front, a firing slot and the eye watching over it."""
    rnd = random.Random(seed)
    P = _root("crate_bunker")
    ground_pad(P, "pad", 6.4, 4.8)
    tapes = [_chevron("Z"), _tape(SP_YELLOW), _hazard_tape("Z"), _tape(SP_RED)]
    S, S2 = (1.15, 1.05, 1.0), (1.05, 0.95, 0.8)
    cells = [(-2.35, -1.7), (-1.15, -1.7), (1.15, -1.7), (2.35, -1.7), (-2.35, -0.6), (2.35, -0.6), (-2.35, 0.5), (2.35, 0.5),
             (-2.35, 1.6), (-1.15, 1.7)]
    for k, (x, y) in enumerate(cells):
        yaw = rnd.uniform(-4, 4)
        crate(P, f"crate{k}", (x, y, 0.08), S, yaw=yaw, shade=k)
        band_box(P, f"band{k}", (x, y, 0), S, 0.08 + rnd.uniform(0.35, 0.65), 0.16, tapes[k % 4], rot=(0, 0, yaw))
        if k < 6:       # the front and the front of the sides go two high: cartons and crates
            z = 1.08
            if k % 2:
                carton(P, f"top{k}", (x, y, z), (1.0, 0.9, 0.8), yaw=rnd.uniform(-6, 6), seed=k, tape=tapes[(k + 1) % 4])
            else:
                crate(P, f"top{k}", (x, y, z), S2, yaw=rnd.uniform(-5, 5), shade=k + 1)
                band_box(P, f"topband{k}", (x, y, 0), S2, z + 0.4, 0.14, tapes[(k + 2) % 4])
    plank(P, "slot_lintel", (2.7, 0.3, 0.08), (0, -1.75, 1.92), shade=2)
    for x in (-1.0, 1.0):
        plank(P, f"slot_post{x}", (0.1, 0.1, 0.9), (x * 0.62, -2.0, 1.45), shade=3)
    # bubble-wrap rolls laid along the wall tops as sandbags, banded with tape
    r = 0.3
    for k, (a, b) in enumerate((((-2.95, -1.75), (-0.75, -1.75)), ((0.75, -1.75), (2.95, -1.75)))):
        a, b = Vector((a[0], a[1], 1.88 + r)), Vector((b[0], b[1], 1.88 + r))
        ob = rod(P, f"wrap{k}", a, b, r, _bubble(), segs=16)
        for t in (0.2, 0.8):
            band = K.cyl(f"wrapband{k}", r + 0.012, 0.1, tapes[(k + 1) % 4], segs=16, bev=0, parent=P)
            band.location = a + (b - a) * t
            band.rotation_euler = ob.rotation_euler
    # a bubble-wrap blanket hung down the left front, taped at its corners
    K.box("blanket", (2.0, 0.05, 1.7), _bubble(), loc=(-1.75, -2.33, 1.0), rot=(-4, 0, 1), bev=0.02, parent=P)
    for x in (-2.6, -0.9):
        K.box("blanket_tape", (0.16, 0.012, 0.5), _chevron("X"), loc=(x, -2.37, 1.7), rot=(0, 30 if x < -1 else -30, 0), bev=0, parent=P)
    eye_lamp(P, (2.35, -0.75, 2.5), 0.17, lit=True)
    K.box("eye_post", (0.08, 0.08, 0.6), _rack(), loc=(2.35, -0.55, 2.18), bev=0, parent=P)
    tape_roll(P, "spare", (-0.9, 0.6, 0.39), 0.32, 0.3, _tape(SP_YELLOW), SP_YELLOW, axis="z")
    tape_roll(P, "spare2", (0.2, 1.4, 0.32), 0.25, 0.28, _chevron("Z"), TAPE_WHITE, axis="y")
    # collision: the U of crates, two high at the front (sandbags on top) either side of the firing gap you can duck
    # through, one high down the sides and back, open at the back right
    for s in (-1, 1):
        K.collider_box(P, f"front{s}", (2.4, 1.1, 2.5), (s * 1.75, -1.7, 1.25))
        K.collider_box(P, f"side_hi{s}", (1.2, 1.1, 1.9), (s * 2.35, -0.6, 0.95))
    K.collider_box(P, "side_l", (1.2, 2.2, 1.1), (-2.35, 1.05, 0.55))
    K.collider_box(P, "side_r", (1.2, 1.1, 1.1), (2.35, 0.5, 0.55))
    K.collider_box(P, "back", (1.2, 1.1, 1.1), (-1.15, 1.7, 0.55))
    K.collider_box(P, "lintel", (2.7, 0.3, 0.1), (0, -1.75, 1.92))
    return P


def spooler_sniper_gantry(seed=104):
    """A sniper nest on a gantry: two bays of warehouse pallet racking with a pallet deck at 4.5 m, a bubble-wrap and
    carton parapet, a long tape gun on a pivot with its scope, and tape curtains hung off the deck for cover."""
    rnd = random.Random(seed)
    P = _root("sniper_gantry")
    rk = _rack()
    beamc = _paint(SP_BLUE, metal=0.3)
    xs = (-2.7, 0.0, 2.7)
    ys = (-0.55, 0.55)
    Hu, Dz = 5.7, 4.5
    for x in xs:
        for y in ys:
            K.box("upright", (0.1, 0.09, Hu), rk, loc=(x, y, Hu / 2), bev=0.01, parent=P)
            K.box("baseplate", (0.22, 0.2, 0.03), rk, loc=(x, y, 0.015), bev=0, parent=P)
        for z0 in (0.3, 1.6, 2.9, 4.1):
            beam(P, "zig", (x, ys[0], z0), (x, ys[1], z0 + 0.65), 0.04, rk, bev=0)
        for z in (0.3, 1.6, 2.9, 4.1, 5.4):
            beam(P, "tie", (x, ys[0], z), (x, ys[1], z), 0.04, rk, bev=0)
    for i in range(2):
        xa, xb = xs[i] + 0.06, xs[i + 1] - 0.06
        for y in ys:
            for z in (1.6, Dz):
                K.box("rack_beam", (xb - xa, 0.05, 0.13), beamc, loc=((xa + xb) / 2, y, z), bev=0.008, parent=P)
        # a pallet of cartons left on the first level
        pallet_flat(P, f"pallet{i}", ((xa + xb) / 2, 0, 1.67), yaw=90)
        for j in range(2):
            carton(P, f"stock{i}{j}", ((xa + xb) / 2 + (j - 0.5) * 0.6, 0, 1.82), (0.55, 0.8, 0.55), yaw=rnd.uniform(-5, 5), seed=i * 3 + j)
    # the nest deck: pallets laid across the top beams
    for k, x in enumerate((-2.1, -0.75, 0.75, 2.1)):
        pallet_flat(P, f"deck{k}", (x, 0, Dz + 0.065), yaw=90 + rnd.uniform(-3, 3))
    # parapet: bubble-wrap rolls and cartons along the front edge
    pz = Dz + 0.2
    for k, x in enumerate((-2.0, 0.0)):
        ob = rod(P, f"bubble{k}", (x - 0.9, -0.62, pz + 0.27), (x + 0.9, -0.62, pz + 0.27), 0.27, _bubble(), segs=16)
        for t in (-0.5, 0.5):
            K.cyl("bubble_band", 0.282, 0.1, _tape(SP_RED), loc=(x + t, -0.62, pz + 0.27), rot=(0, 90, 0), segs=16, bev=0, parent=P)
    carton(P, "parapet_box", (2.1, -0.4, pz), (0.8, 0.6, 0.6), yaw=6, seed=7, tape=_hazard_tape("Y"))
    # the long tape gun on a pivot post: ribbed barrel, magazine roll, scope
    G = _empty(P, "gun", (1.05, -0.1, pz + 0.85), (0, 0, -12))
    K.cyl("pivot", 0.07, 0.85, rk, loc=(0, 0, -0.42), segs=10, bev=0, parent=P).parent = G
    K.box("gun_body", (0.36, 0.9, 0.34), rk, loc=(0, 0.1, 0.12), bev=0.02, parent=G)
    K.cyl("barrel", 0.08, 1.8, _bare(), loc=(0, -1.15, 0.14), rot=(90, 0, 0), segs=12, bev=0.005, parent=G)
    for k in range(6):
        K.cyl("rib", 0.1, 0.05, rk, loc=(0, -0.5 - k * 0.22, 0.14), rot=(90, 0, 0), segs=12, bev=0, parent=G)
    K.cyl("muzzle", 0.12, 0.16, rk, loc=(0, -2.02, 0.14), rot=(90, 0, 0), segs=12, bev=0.01, parent=G)
    tape_roll(G, "mag", (0, 0.35, 0.48), 0.3, 0.24, _hazard_tape("Z"), HAZ_Y, axis="x")
    K.cyl("scope", 0.06, 0.65, _dark(), loc=(0.24, -0.15, 0.42), rot=(90, 0, 0), segs=10, bev=0.005, parent=G)
    K.cyl("scope_lens", 0.045, 0.02, _eye(), loc=(0.24, -0.48, 0.42), rot=(90, 0, 0), segs=10, bev=0, parent=G)
    # tape curtains hung off the deck edge: strips of chevron and red tape for cover
    for k in range(14):
        x = -2.6 + k * 0.4 + rnd.uniform(-0.05, 0.05)
        L = rnd.uniform(1.0, 2.4)
        K.box("curtain", (0.12, 0.006, L), [_chevron("X"), _tape(SP_RED), _tape(SP_YELLOW)][k % 3], loc=(x, -0.68, Dz - L / 2),
              rot=(rnd.uniform(-4, 4), 0, rnd.uniform(-6, 6)), bev=0, parent=P)
    ladder(P, (2.7, 0.72, 0), 180, 0.0, Dz + 0.2, cage_from=None, standoff=0.08, mat=_zinc(), top_ext=1.0)
    ground_pad(P, "pad", 6.6, 1.8, h=0.06)
    return P


def spooler_cocoon(seed=105):
    """A bubble-wrap cocoon hung from a tripod of salvaged pipe: something wrapped up tight in bubble wrap and tape, a
    Scuttler's leg still sticking out of it."""
    P = _root("cocoon")
    rk = _rack()
    top = Vector((0, 0, 2.9))
    for k in range(3):
        a = math.radians(90 + 120 * k)
        foot = Vector((math.cos(a) * 1.1, math.sin(a) * 1.1, 0.0))
        rod(P, f"leg{k}", foot + Vector((0, 0, -0.1)), top + Vector((0, 0, 0.12)), 0.045, rk, segs=10)
        K.box(f"foot{k}", (0.16, 0.16, 0.05), rk, loc=(foot.x, foot.y, 0.02), bev=0, parent=P)
    K.cyl("lash", 0.1, 0.25, _tape(SP_YELLOW), loc=(0, 0, 2.95), segs=12, bev=0, parent=P)
    rod(P, "hang", (0, 0, 2.9), (0, 0, 2.3), 0.025, _tape(SP_YELLOW), segs=6)
    # the cocoon: a long bubble-wrap bundle bound with bands of tape, a little twisted
    G = _empty(P, "pod", (0, 0, 1.38), (4, -3, 18))
    K.sphere("pod", 0.5, _bubble(), scale=(0.85, 0.75, 1.9), parent=G)
    K.sphere("pod_bulge", 0.36, _bubble(), loc=(0.12, -0.08, -0.2), scale=(1.0, 1.0, 1.4), parent=G)
    K.sphere("pod_bulge2", 0.3, _bubble(), loc=(-0.1, 0.1, 0.35), scale=(1.0, 0.9, 1.3), parent=G)
    K.cyl("pod_neck", 0.2, 0.4, _bubble(), loc=(0, 0, 1.0), segs=12, r2=0.06, bev=0.01, parent=G)
    K.cyl("pod_tie", 0.11, 0.08, _tape(SP_YELLOW), loc=(0, 0, 1.08), segs=12, bev=0, parent=G)
    for k, (z, r, m) in enumerate(((0.62, 0.36, _chevron("Z")), (0.25, 0.42, _tape(SP_YELLOW)), (-0.15, 0.42, _chevron("Z")),
                                    (-0.55, 0.34, _tape(SP_YELLOW)))):
        b = K.cyl(f"bind{k}", r, 0.09, m, loc=(0, 0, z), segs=18, bev=0, parent=G)
        b.scale = (1.0, 0.9, 1.0)
    K.box("tag", (0.18, 0.006, 0.12), _kraft(), loc=(0.14, -0.4, 0.3), rot=(0, 12, 0), bev=0, parent=G)
    # a green Scuttler's leg poking out of the bottom
    rod(P, "victim_leg", (0.08, -0.05, 0.62), (0.32, -0.35, 0.22), 0.035, _paint("#5fae3e", metal=0.3))
    K.box("victim_foot", (0.12, 0.2, 0.05), _dark(), loc=(0.34, -0.39, 0.19), rot=(20, 0, 40), bev=0.01, parent=P)
    return P


def spooler_sentinel(seed=106):
    """A cardboard-and-tape sentinel: a Spooler effigy that marks the faction's ground. A post in a taped tyre, a carton
    body, a head carton with a dead lens and a reel off its left side, an antenna flying a tape pennant."""
    P = _root("sentinel")
    rk = _rack()
    K.cyl("tyre", 0.36, 0.22, _tyre_mat(), loc=(0, 0, 0.11), segs=18, bev=0.05, parent=P)
    K.cyl("fill", 0.24, 0.2, _soil(), loc=(0, 0, 0.13), segs=12, bev=0, parent=P)
    K.cyl("tyre_tape", 0.365, 0.07, _hazard_tape("Z"), loc=(0, 0, 0.12), segs=18, bev=0, parent=P)
    K.box("post", (0.07, 0.07, 1.3), rk, loc=(0, 0, 0.75), bev=0, parent=P)
    carton(P, "body", (0, 0, 0.95), (0.5, 0.38, 0.55), yaw=-4, seed=3, tape=_chevron("Y"))
    band_box(P, "body_band", (0, 0, 0), (0.5, 0.38, 0.55), 1.2, 0.08, _tape(SP_YELLOW), rot=(0, 0, -4))
    G = _empty(P, "head", (0, 0, 1.52), (0, 0, 7))
    carton(G, "head_box", (0, 0, 0), (0.52, 0.46, 0.45), seed=4, tape=_tape(SP_RED))
    eye_lamp(G, (0.06, -0.23, 0.24), 0.1, lit=False)
    for k in range(3):
        K.box("mouth", (0.2, 0.006, 0.025), _dark(), loc=(0.06, -0.234, 0.08 + k * 0.04), bev=0, parent=G)
    tape_roll(G, "reel", (-0.33, 0, 0.24), 0.17, 0.1, _chevron("Z"), TAPE_WHITE, axis="x")
    rod(G, "axle", (-0.26, 0, 0.24), (-0.4, 0, 0.24), 0.015, _bare())
    rod(G, "antenna", (0.15, 0.1, 0.45), (0.15, 0.1, 1.1), 0.012, _bare(), segs=6)
    K.box("pennant", (0.004, 0.26, 0.12), _chevron("X"), loc=(0.15, 0.23, 1.03), bev=0, parent=G)
    # arms: two short sticks of racking taped on, a tape roll for a gun hand
    rod(P, "arm_r", (0.25, 0, 1.3), (0.55, -0.2, 1.05), 0.02, rk)
    tape_roll(P, "gun_roll", (0.57, -0.22, 1.02), 0.08, 0.07, _tape(SP_YELLOW), SP_YELLOW, axis="x")
    rod(P, "arm_l", (-0.25, 0, 1.3), (-0.5, -0.1, 0.95), 0.02, rk)
    return P


def spooler_reel_barricade(seed=107):
    """A barricade of two giant cable drums rolled into a gap and chocked, joined by a pipe, wound with tape and strung
    across with hazard tape; a pallet propped against them."""
    P = _root("reel_barricade")
    R, w = 0.9, 1.0
    for s, tape in ((-1, _chevron("Z")), (1, _tape(SP_RED))):
        x = s * 0.85
        spool(P, f"drum{s}", (x, 0, R), R, w, tape, axis="x", core=0.3, flange=wood(1, wear=0.3), ribs=4)
        for t in (-1, 1):
            K.box(f"chock{s}{t}", (0.4, 0.25, 0.2), wood(3), loc=(x, t * (R * 0.75), 0.1), rot=(t * -25, 0, 0), bev=0.01, parent=P)
    rod(P, "pipe", (-1.75, 0, R), (1.75, 0, R), 0.07, _bare(), segs=10)
    for z in (0.55, 1.35):
        ribbon(P, f"haz{z}", (-1.6, -0.25, z), (1.6, -0.25, z + 0.1), 0.12, _hazard_tape("X"), sag=0.06, n=2)
    G = _empty(P, "pallet_lean", (0.0, 0.85, 0), (70, 0, 0))
    pallet_flat(G, "pallet", (0, 0.0, 0), yaw=0)
    return P



# ================================================================== Threshers
def _grey(**kw):
    return _paint(TH_GREY, metal=0.3, **kw)


def _mustard(**kw):
    return _paint(TH_MUSTARD, **kw)


def _till(**kw):
    return _paint(TILL_RED, **kw)


def _cream(**kw):
    return _paint(TILL_CREAM, **kw)


def _olive(**kw):
    return _paint(OLIVE, **kw)


def _tank():
    return _paint(TANK, wear=0.2, grime=0.55)


def _rusty():
    return M(RUST, metal=0.4, wear=0.2, grime=0.5, rough=0.8)


def _steel():
    return M("#8d9096", metal=0.7, wear=0.1, grime=0.4, rough=0.35)


def ag_tyre(P, name, loc, r, w, rot=(0, 0, 0), lugs=14, rim=None):
    """A lugged tractor tyre on its rim, its axle along local Y (rot (90, 0, 0) lays it flat)."""
    G = _empty(P, name, loc, rot)
    tyre(G, name, (0, 0, 0), r, w, axis="y", lugs=lugs, rim=rim or _cream(wear=0.3))
    return G


def lite_tyre(P, name, loc, r, w, lugs=8, rim=None):
    """A cheaper lugged tyre for the lairs, axle along local Y: no bevels, fewer sides, chevron lug pairs."""
    G = _empty(P, name, loc, (90, 0, 0))
    tm = _tyre_mat()
    K.cyl(f"{name}_tyre", r, w, tm, segs=12, bev=0, parent=G)
    K.cyl(f"{name}_rim", r * 0.55, w + 0.02, rim or _cream(wear=0.3), segs=10, bev=0, parent=G)
    K.cyl(f"{name}_hub", r * 0.18, w + 0.06, M(GRAPHITE, metal=0.5, wear=0.5), segs=6, bev=0, parent=G)
    for k in range(lugs):
        a = 2 * math.pi * k / lugs
        for s in (-1, 1):
            K.box(f"{name}_lug", (0.09, 0.09, w * 0.5), tm, loc=(math.cos(a) * r, math.sin(a) * r, s * w * 0.25),
                  rot=(s * 20, 0, math.degrees(a)), bev=0, parent=G)
    return G


def hazard_bands(P, name, a, b, w, n, rot_box=None):
    """A square post from a to b painted in yellow and black bands (n bands)."""
    a, b = Vector(a), Vector(b)
    for k in range(n):
        p0, p1 = a.lerp(b, k / n), a.lerp(b, (k + 1) / n)
        beam(P, f"{name}{k}", p0, p1, w, M(HAZ_YEL if k % 2 == 0 else HAZ_BLK, wear=0.25, grime=0.4), bev=0)


def stripes_y(P, loc, w, h, n=6):
    """Diagonal-looking hazard stripes on a face towards -Y (the thresher bots' yellow and black)."""
    hazard(P, loc, w, h, M(HAZ_YEL, wear=0.25, grime=0.3), M(HAZ_BLK), n=n)


def mist(P, name, loc, r, n, rnd, rise=0.5, strength=1.6, lite=False):
    """A drifting puff of pesticide: flattened glowing lumps rising and spreading from loc (lite: coarser lumps)."""
    import bmesh
    x, y, z = loc
    for k in range(n):
        rr = r * (0.7 + 0.5 * k / max(1, n - 1))
        at = (x + rnd.uniform(-r, r) * 0.6, y + rnd.uniform(-r, r) * 0.6, z + k * rise)
        if lite:
            bm = bmesh.new()
            bmesh.ops.create_uvsphere(bm, u_segments=7, v_segments=4, radius=rr * 1.3)
            ob = K.from_bm(f"{name}{k}", bm, _mist_mat(), smooth=True)
            ob.scale = (1.25, 1.1, 0.8)
            K.place(ob, at, (0, 0, 0), P)
        else:
            K.sphere(f"{name}{k}", rr * 1.3, _mist_mat(), loc=at, scale=(1.25, 1.1, 0.8), parent=P)


def furrows(P, name, center, L, n, pitch=0.42, yaw=0.0, h=0.18, rnd=None, clods=0, coarse=False):
    """Tilled ground: n ridges of turned soil, L long (along local x), pitch apart, as one low heightfield whose ridges
    die into the ground at the ends and sides; a few clods on top."""
    import bmesh
    G = _empty(P, name, center, (0, 0, yaw))
    rnd = rnd or random.Random(1)
    W = n * pitch
    nx, ny = (max(5, int(L / 0.8)), n * 3) if coarse else (max(6, int(L / 0.45)), n * 4)
    bm = bmesh.new()
    vs = []
    for i in range(nx + 1):
        row = []
        for j in range(ny + 1):
            u, v = i / nx, j / ny
            x = (u - 0.5) * L + (rnd.uniform(-0.06, 0.06) if 0 < i < nx else 0)
            y = (v - 0.5) * W
            fall = min(1.0, min(u, 1 - u) * 6) * min(1.0, min(v, 1 - v) * 8 + 0.15)
            z = h * (0.5 - 0.5 * math.cos(2 * math.pi * v * n)) * fall * rnd.uniform(0.75, 1.15) + 0.01
            if i in (0, nx) or j in (0, ny):
                z = -0.03
                x += rnd.uniform(-0.12, 0.12)
                y += rnd.uniform(-0.08, 0.08) * (1 if j in (0, ny) else 0)
            row.append(bm.verts.new((x, y, z)))
        vs.append(row)
    for i in range(nx):
        for j in range(ny):
            bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
    K.place(K.from_bm(f"{name}_field", bm, _soil()), parent=G)
    for k in range(n):
        y = (k - (n - 1) / 2) * pitch
        for j in range(clods):
            lump(G, f"{name}_clod", rnd.uniform(0.05, 0.1), _soil(rnd.random() < 0.3), (-L / 2 + L * rnd.uniform(0.1, 0.9), y + rnd.uniform(-0.1, 0.1), h * 0.8), rnd)
    return G


def header(P, name, loc, yaw=0.0, W=6.0, tilt=(0, 0), rnd=None, reel=True, scale=1.0, lite=False):
    """A combine's cutting header torn off its machine: a pan and back wall between pointed crop dividers, a sickle bar
    of knife guards along the front, a cross auger inside and the bat reel above, hazard stripes on its ends.
    Front (the teeth) towards local -Y. lite keeps the silhouette with fewer guards, flights and bats (the den's ring)."""
    rnd = rnd or random.Random(1)
    G = _empty(P, name, loc, (tilt[0], tilt[1], yaw))
    G.scale = (scale, scale, scale)
    g, mu, dk = _grey(), _mustard(), _dark()
    K.box(f"{name}_pan", (W, 1.5, 0.08), g, loc=(0, -0.05, 0.12), bev=0, parent=G)
    eb = 0 if lite else 0.02
    K.box(f"{name}_back", (W, 0.1, 1.3), g, loc=(0, 0.72, 0.75), rot=(-8, 0, 0), bev=eb, parent=G)
    K.box(f"{name}_toprail", (W + 0.1, 0.22, 0.16), mu, loc=(0, 0.78, 1.42), bev=eb, parent=G)
    K.box(f"{name}_curve", (W, 0.5, 0.08), g, loc=(0, 0.48, 0.28), rot=(50, 0, 0), bev=0, parent=G)
    for s in (-1, 1):
        G2 = _empty(G, f"{name}_end", (s * W / 2, 0, 0), (0, 0, 90))
        plate_xz(f"{name}_divider", [(-1.05, 0.05), (0.8, 0.05), (0.8, 1.5), (0.15, 1.5), (-0.55, 0.45)], 0.08, mu, parent=G2, bev=0 if lite else 0.01)
        hazard(G2, (0.45, -0.05, 1.0), 0.6, 0.35, M(HAZ_YEL, wear=0.2), M(HAZ_BLK), n=2 if lite else 4, axis="y")
        if not lite:
            K.box(f"{name}_skid", (0.12, 1.7, 0.08), dk, loc=(s * (W / 2 - 0.1), -0.1, 0.04), bev=0, parent=G)
    # the sickle bar: a bar of pointed knife guards along the front edge
    K.box(f"{name}_bar", (W, 0.14, 0.07), dk, loc=(0, -0.82, 0.1), bev=0, parent=G)
    n = int(W / (1.0 if lite else 0.4))
    for k in range(n):
        x = -W / 2 + 0.15 + k * W / n
        K.cyl(f"{name}_guard", 0.045, 0.32, _steel(), loc=(x, -1.0, 0.1), rot=(90, 0, 0), segs=4, r2=0.005, bev=0, parent=G)
    # the cross auger: a tube with a few flights
    K.cyl(f"{name}_auger", 0.24, W - 0.2, g, loc=(0, 0.25, 0.42), rot=(0, 90, 0), segs=8 if lite else 12, bev=0, parent=G)
    for k in range(0 if lite else 3):
        K.cyl(f"{name}_flight", 0.36, 0.03, mu, loc=(-W / 2 + 0.8 + k * (W - 1.6) / 2, 0.25, 0.42), rot=(0, 90 + (12 if k % 2 else -12), 0),
              segs=10, bev=0, parent=G)
    if reel:
        # the bat reel on two arms, its bats mustard
        rc = Vector((0, -0.45, 1.55))
        for s in (-1, 1):
            beam(G, f"{name}_arm", (s * (W / 2 - 0.1), 0.75, 1.45), (s * (W / 2 - 0.1), rc.y, rc.z), 0.12, dk, bev=0)
            K.cyl(f"{name}_spider", 0.16, 0.12, dk, loc=(s * (W / 2 - 0.25), rc.y, rc.z), rot=(0, 90, 0), segs=8, bev=0, parent=G)
        K.cyl(f"{name}_shaft", 0.06, W - 0.3, _steel(), loc=rc, rot=(0, 90, 0), segs=8, bev=0, parent=G)
        a0 = rnd.uniform(0, 60)
        bats = 4 if lite else 5
        for k in range(bats):
            a = math.radians(a0 + 360 / bats * k)
            p = rc + Vector((0, math.cos(a) * 0.7, math.sin(a) * 0.7))
            K.box(f"{name}_bat", (W - 0.5, 0.07, 0.12), mu, loc=p, rot=(math.degrees(a), 0, 0), bev=0, parent=G)
            for s in ((-1, 1) if not lite else ()):
                beam(G, f"{name}_spoke", (s * (W / 2 - 0.4) - 0.0, rc.y, rc.z), (s * (W / 2 - 0.4), p.y, p.z), 0.05, dk, bev=0)
    return G


def thresher_den(seed=201):
    """The Threshers' den: a 21 m ring of torn-off harvester headers, teeth out, round a gutted combine with its
    unloading auger raised as a 16 m mast that carries spray booms and a beacon; a leaking pesticide tank, tilled
    scars inside the ring and running out of its gate."""
    rnd = random.Random(seed)
    P = _root("thresher_den")
    K.disc("den_ground", 11.2, _soil(True), (0, 0), z=0.012, segs=40, wobble=0.4, seed=seed).parent = P
    # the ring of headers, open at the front (the gate)
    Rr = 10.4
    for k in range(6):
        a = math.radians(-90 + 360 / 7 * (k + 1))
        x, y = math.cos(a) * Rr, math.sin(a) * Rr
        yaw = math.degrees(a) + 90         # the header's front (-Y) faces out
        # tipped back on its rear edge so the knife guards rear up and out at the world
        G = header(P, f"hdr{k}", (x, y, 0.38), yaw=yaw, W=6.0, tilt=(-24 + rnd.uniform(-3, 3), rnd.uniform(-2, 2)), rnd=rnd, reel=k in (1, 4), scale=1.4, lite=True)
        K.collider_box(G, f"hdr{k}", (6.0, 1.9, 1.5), (0, -0.1, 0.75))
    # gate posts: two hazard-banded stakes either side of the gap, a sickle bar slung between them
    for s in (-1, 1):
        x = s * 2.6
        hazard_bands(P, f"gate{s}", (x, -10.2, -0.2), (x, -10.2, 4.2), 0.26, 5)
        K.collider_box(P, f"gate{s}", (0.3, 0.3, 4.4), (x, -10.2, 2.0))
    K.box("gate_bar", (5.6, 0.14, 0.12), _dark(), loc=(0, -10.2, 3.95), bev=0, parent=P)
    for k in range(9):
        K.cyl("gate_tooth", 0.07, 0.48, _steel(), loc=(-2.45 + k * 0.612, -10.2, 3.68), rot=(180, 0, 0), segs=4, r2=0.005, bev=0, parent=P)
    # tilled scars: furrows inside the ring and two running out through the gate
    furrows(P, "scar_in", (-3.6, -3.2, 0.0), 6.0, 5, yaw=35, rnd=rnd, coarse=True)
    furrows(P, "scar_out", (0.2, -13.6, 0.0), 6.0, 4, yaw=82, rnd=rnd, coarse=True)
    furrows(P, "scar_side", (11.5, 4.5, 0.0), 5.0, 3, yaw=-25, rnd=rnd, coarse=True)
    # the combine hulk: body, grain tank, cab, on its wheels, its side panels torn away to the ribs
    C = _empty(P, "combine", (1.0, 2.6, 0.0), (0, 0, -18))
    C.scale = (1.25, 1.25, 1.25)
    g, mu, dk = _grey(), _mustard(), _dark()
    K.box("chassis", (6.4, 2.6, 0.4), dk, loc=(0, 0, 1.0), bev=0, parent=C)
    # collision: the hulk on its wheels, its tank and cab, the feederhouse
    K.collider_box(C, "hulk", (6.6, 4.0, 3.9), (-0.1, 0, 2.15))
    K.collider_box(C, "tank", (3.6, 3.3, 1.5), (0.9, 0, 4.85))
    K.collider_box(C, "cab", (2.3, 2.5, 1.6), (-2.9, 0, 3.9))
    K.collider_box(C, "feeder", (2.4, 1.5, 1.1), (-4.3, 0, 1.55), (0, -28, 0))
    K.box("body", (4.9, 2.7, 3.0), g, loc=(0.4, 0, 2.7), bev=0, parent=C)
    for a in (-1, 1):           # the frame's corner posts and top rails
        for b in (-1, 1):
            K.box("body_post", (0.22, 0.22, 3.0), dk, loc=(0.4 + a * 2.45, b * 1.35, 2.7), bev=0, parent=C)
        K.box("body_rail", (5.1, 0.22, 0.22), dk, loc=(0.4, a * 1.35, 4.2), bev=0, parent=C)
    K.box("tank", (3.4, 3.1, 1.3), g, loc=(0.9, 0, 4.85), bev=0, parent=C)
    K.box("tank_rim", (3.6, 3.3, 0.18), mu, loc=(0.9, 0, 5.55), bev=0, parent=C)
    for k in range(4):          # torn side: the ribs show through a hole in the -Y panels
        K.box("rib", (0.12, 0.2, 2.3), _rusty(), loc=(-0.6 + k * 0.7, -1.47, 2.6), rot=(0, rnd.uniform(-6, 6), 0), bev=0, parent=C)
    K.box("torn_hole", (2.6, 0.06, 2.0), K.mat("hole", "#1a1c1f", rough=0.9), loc=(0.45, -1.39, 2.6), bev=0, parent=C)
    K.box("flap", (1.2, 0.05, 1.4), g, loc=(-1.0, -2.0, 1.25), rot=(0, 12, -35), bev=0, parent=C)
    stripes_y(C, (0.4, -1.45, 1.35), 4.6, 0.22, n=6)
    # the feederhouse: the chute the header hung on, now bare, its chain slats showing
    K.box("feeder", (2.4, 1.5, 1.1), g, loc=(-4.3, 0, 1.55), rot=(0, -28, 0), bev=0, parent=C)
    K.box("feeder_mouth", (0.1, 1.5, 1.1), K.mat("hole", "#1a1c1f", rough=0.9), loc=(-5.38, 0, 1.0), rot=(0, -28, 0), bev=0, parent=C)
    for k in range(3):
        K.box("feeder_slat", (0.06, 1.3, 0.06), mu, loc=(-5.44 + k * 0.05, 0, 0.7 + k * 0.28), rot=(0, -28, 0), bev=0, parent=C)
    # the cab at the front, its eye long dead
    K.box("cab", (2.0, 2.2, 1.9), g, loc=(-2.9, 0, 3.45), bev=0, parent=C)
    K.box("cab_roof", (2.3, 2.5, 0.16), mu, loc=(-2.9, 0, 4.48), bev=0, parent=C)
    K.box("cab_glass", (0.06, 1.8, 1.1), K.mat("darkglass", "#22303a", rough=0.15, metal=0.2), loc=(-3.92, 0, 3.6), bev=0, parent=C)
    K.cyl("cab_eye", 0.32, 0.12, K.mat("deadeye", "#2a3a30", rough=0.15, metal=0.2), loc=(-3.97, 0, 3.6), rot=(0, 90, 0), segs=10, bev=0, parent=C)
    K.cyl("cab_eye_rim", 0.4, 0.1, dk, loc=(-3.95, 0, 3.6), rot=(0, 90, 0), segs=10, bev=0, parent=C)
    for s in (-1, 1):
        K.cyl("stack", 0.13, 1.6, dk, loc=(2.3, s * 0.7, 4.9), segs=6, bev=0, parent=C)
        lite_tyre(C, f"front{s}", (-1.7, s * 1.65, 1.0), 1.0, 0.65, lugs=6, rim=_mustard(wear=0.3))
        lite_tyre(C, f"rear{s}", (2.4, s * 1.45, 0.62), 0.62, 0.4, lugs=5, rim=_mustard(wear=0.3))
    # the unloading auger raised as a mast from the tank's back corner, guyed, booms and a beacon at its head
    base = Vector((2.4, 1.2, 5.4))
    top = Vector((3.9, 2.6, 15.0))
    rod(C, "auger_mast", base, top, 0.3, g, segs=10)
    K.collider_only(rod(C, "col_mast", base, top, 0.32, None, segs=6))
    for t in (0.33, 0.66):
        K.cyl("mast_band", 0.33, 0.25, mu, loc=base.lerp(top, t), segs=10, bev=0, parent=C).rotation_euler = (top - base).to_track_quat("Z", "Y").to_euler()
    rod(C, "mast_strut", (1.0, 1.4, 5.5), base.lerp(top, 0.3), 0.12, dk)
    K.cyl("elbow", 0.34, 0.68, g, loc=top, segs=8, bev=0, parent=C)
    tip = top + Vector((1.7, 1.1, -0.2))
    rod(C, "auger_head", top, tip, 0.28, g, segs=8)
    K.cyl("spout", 0.3, 0.9, mu, loc=tip + Vector((0, 0, -0.45)), segs=8, r2=0.2, bev=0, parent=C)
    # the booms: a light truss across the mast head, nozzles along it, a hazard tip at each end
    bz = top.z - 0.9
    for s in (-1, 1):
        e = Vector((top.x + s * 0.0, top.y + s * 5.0, bz - 0.5))
        rod(C, "boom_lo", (top.x, top.y, bz), e, 0.07, _olive())
        rod(C, "boom_hi", (top.x, top.y, bz + 0.6), e + Vector((0, 0, 0.1)), 0.05, _olive())
        for k in range(1, 4):
            q = Vector((top.x, top.y, bz)).lerp(e, k / 4)
            K.box("nozzle", (0.08, 0.08, 0.2), dk, loc=q - Vector((0, 0, 0.12)), bev=0, parent=C)
        K.box("boom_tip", (0.22, 0.4, 0.22), M(HAZ_YEL, wear=0.25), loc=e, bev=0, parent=C)
        rod(C, "guy", top - Vector((0, 0, 1.2)), (top.x + s * 7.0, top.y - 2.0 + s * 3.0, 0.0), 0.025, dk, segs=4)
    K.cyl("beacon_base", 0.22, 0.2, dk, loc=top + Vector((0, 0, 0.85)), segs=8, bev=0, parent=C)
    K.cyl("beacon", 0.18, 0.35, K.mat("beacon", HAZ_YEL, rough=0.2, emit=HAZ_YEL, emit_strength=2.5), loc=top + Vector((0, 0, 1.12)), segs=8, bev=0, parent=C)
    mist(C, "boom_mist", (top.x, top.y - 3.5, bz - 1.1), 0.35, 2, rnd, rise=-0.5, lite=True)
    # a pesticide tank on its side by the hulk, leaking into a green pool
    T = _empty(P, "tank_grp", (-5.0, 3.6, 0), (0, 0, 30))
    K.cyl("ptank", 1.0, 3.2, _tank(), loc=(0, 0, 1.15), rot=(0, 90, 0), segs=14, bev=0.08, parent=T)
    K.collider_box(T, "ptank", (3.3, 2.0, 2.15), (0, 0, 1.07))
    for x in (-1.0, 1.0):
        K.cyl("ptank_band", 1.03, 0.16, _olive(), loc=(x, 0, 1.15), rot=(0, 90, 0), segs=14, bev=0, parent=T)
        K.box("cradle", (0.25, 1.6, 0.3), dk, loc=(x, 0, 0.15), bev=0, parent=T)
    hazard_diamond(T, (0, -1.02, 1.3), (90, 0, 0), s=0.6, mark="drop")
    K.cyl("vent", 0.1, 0.8, dk, loc=(0.6, 0, 2.4), segs=10, bev=0, parent=T)
    mist(T, "vent_mist", (0.6, 0, 3.0), 0.3, 2, rnd, rise=0.45, lite=True)
    K.disc("spill", 2.3, K.mat("spill", SPILL, rough=0.25), (-5.6, 1.6), z=0.02, segs=24, wobble=0.5, seed=seed + 1).parent = P
    K.disc("spill_fresh", 0.9, _pest(0.6), (-5.3, 2.3), z=0.026, segs=16, wobble=0.4, seed=seed + 2).parent = P
    return P


def thresher_pesticide_shrine(seed=202):
    """A pesticide shrine: a chemical tank stood on end on a plinth of tractor ballast weights, three vent pipes
    leaking chartreuse mist, hoses and spray lances laid round it, drums and a stained pool, ringed by hazard stakes."""
    rnd = random.Random(seed)
    P = _root("pesticide_shrine")
    K.disc("stain", 2.8, K.mat("spill", SPILL, rough=0.3), (0, 0), z=0.012, segs=32, wobble=0.45, seed=seed).parent = P
    # plinth: two courses of the Tiller's rust-red ballast weights, a lifting slot in each
    for k in range(4):
        for j in range(2):
            x, y = (k - 1.5) * 0.62, (j - 0.5) * 1.25
            K.box("weight", (0.58, 1.2, 0.42), _till(), loc=(x, y, 0.21), bev=0.03, parent=P)
            K.box("slot", (0.2, 0.02, 0.1), _dark(), loc=(x, y - 0.6, 0.28), bev=0, parent=P)
    for k in range(3):
        x = (k - 1) * 0.66
        K.box("weight2", (0.62, 1.8, 0.4), _till(), loc=(x, 0, 0.62), bev=0.03, parent=P)
        K.box("slot2", (0.22, 0.02, 0.1), _dark(), loc=(x, -0.9, 0.68), bev=0, parent=P)
    # the tank on end, a dome and a manway on top, an olive band and a hazard band
    z0, h, r = 0.82, 2.4, 0.85
    K.cyl("tank", r, h, _tank(), loc=(0, 0, z0 + h / 2), segs=24, bev=0.04, parent=P)
    K.sphere("dome", r, _tank(), loc=(0, 0, z0 + h), scale=(1, 1, 0.4), parent=P)
    K.cyl("skirt", r + 0.06, 0.25, _olive(), loc=(0, 0, z0 + 0.12), segs=24, bev=0.02, parent=P)
    K.cyl("band", r + 0.02, 0.22, _olive(), loc=(0, 0, z0 + h * 0.62), segs=24, bev=0, parent=P)
    for k in range(12):
        a = 2 * math.pi * k / 12
        K.box("haz", (0.46, 0.05, 0.22), M(HAZ_YEL if k % 2 else HAZ_BLK, wear=0.2), loc=(math.cos(a) * (r + 0.01), math.sin(a) * (r + 0.01), z0 + 0.5),
              rot=(0, 0, math.degrees(a) + 90), bev=0, parent=P)
    K.cyl("manway", 0.3, 0.16, _dark(), loc=(0, 0, z0 + h + 0.35), segs=14, bev=0.02, parent=P)
    hazard_diamond(P, (0, -r - 0.02, z0 + 1.2), (90, 0, 0), s=0.55, mark="drop")
    stencil(P, "D-7", (0.0, -r - 0.03, z0 + h * 0.82), 0.22, _dark())
    # three vents rising off the dome, each leaking mist
    for k, a in enumerate((30, 150, 270)):
        a = math.radians(a)
        x, y = math.cos(a) * 0.5, math.sin(a) * 0.5
        top = z0 + h + rnd.uniform(1.1, 1.7)
        K.cyl("vent", 0.08, top - (z0 + h), _dark(), loc=(x, y, (z0 + h + top) / 2), segs=10, bev=0, parent=P)
        K.cyl("vent_cap", 0.13, 0.12, _olive(), loc=(x, y, top), segs=10, bev=0.01, parent=P)
        mist(P, f"mist{k}", (x, y, top + 0.35), 0.24, 4, rnd, rise=0.42)
    # hoses down to spray lances laid in the stain
    for k, (a, b) in enumerate((((0.7, -0.45, 1.2), (1.9, -1.5, 0.06)), ((-0.75, 0.3, 1.0), (-2.0, 1.2, 0.06)))):
        rod(P, f"hose{k}", a, b, 0.05, K.mat("hose", "#1d1f22", rough=0.8), segs=8)
        rod(P, f"lance{k}", b, (b[0] + (0.9 if k == 0 else -0.8), b[1] - 0.4, 0.08), 0.025, _steel(), segs=6)
    # drums round the plinth and a ring of hazard stakes
    drum(P, "drum1", (1.7, 0.9, 0.44), col=OLIVE, wear=0.3)
    drum(P, "drum2", (-1.5, -1.3, 0.31), rot=(90, 0, 25), col=TANK, wear=0.3)
    drum(P, "drum3", (2.25, 0.3, 0.44), col=TANK, wear=0.3)
    for k in range(6):
        a = math.radians(60 * k + 15)
        x, y = math.cos(a) * 2.9, math.sin(a) * 2.9
        hazard_bands(P, f"stake{k}", (x, y, -0.2), (x + rnd.uniform(-0.1, 0.1), y + rnd.uniform(-0.1, 0.1), 1.2), 0.08, 5)
    return P


def thresher_spray_mast(seed=203):
    """A Duster spray mast: three olive stilt legs on narrow wheel feet carry a chemical tank at 5 m, and above it a
    mast with spray booms folded out wide, nozzles dripping mist, a hazard beacon on top."""
    rnd = random.Random(seed)
    P = _root("spray_mast")
    ol, dk = _olive(), _dark()
    hub = Vector((0, 0, 5.0))
    for k in range(3):
        a = math.radians(90 + 120 * k)
        foot = Vector((math.cos(a) * 2.2, math.sin(a) * 2.2, 0.48))
        knee = hub.lerp(foot, 0.5) + Vector((math.cos(a) * 0.35, math.sin(a) * 0.35, 0))
        beam(P, f"thigh{k}", hub + Vector((math.cos(a) * 0.5, math.sin(a) * 0.5, 0)), knee, 0.2, ol, bev=0.02)
        beam(P, f"shin{k}", knee, foot + Vector((0, 0, 0.3)), 0.16, ol, bev=0.02)
        K.sphere(f"knee{k}", 0.16, dk, loc=knee, parent=P)
        hazard_bands(P, f"shinhaz{k}", knee.lerp(foot, 0.55), knee.lerp(foot, 0.75), 0.18, 4)
        W = _empty(P, f"foot{k}", (foot.x, foot.y, 0.48), (0, 0, math.degrees(a)))
        K.box(f"fork{k}", (0.08, 0.3, 0.6), dk, loc=(0.22, 0, 0.2), bev=0, parent=W)
        ag_tyre(W, f"wheel{k}", (0, 0, 0), 0.48, 0.2, rot=(0, 0, 90), lugs=10, rim=M(HAZ_YEL, wear=0.3))
    K.cyl("ring", 0.7, 0.3, dk, loc=hub, segs=16, bev=0.02, parent=P)
    # the tank: a capsule slung in the hub
    K.cyl("tank", 0.85, 1.6, _tank(), loc=(0, 0, 5.75), rot=(0, 90, 0), segs=20, bev=0.08, parent=P)
    for s in (-1, 1):
        K.sphere("tank_end", 0.85, _tank(), loc=(s * 0.8, 0, 5.75), scale=(0.35, 1, 1), parent=P)
        K.cyl("tank_band", 0.88, 0.14, ol, loc=(s * 0.45, 0, 5.75), rot=(0, 90, 0), segs=20, bev=0, parent=P)
    hazard_diamond(P, (0, -0.86, 5.75), (90, 0, 0), s=0.45, mark="drop")
    # the mast and the booms
    K.box("mast", (0.2, 0.2, 2.2), ol, loc=(0, 0, 7.6), bev=0.02, parent=P)
    bz = 7.9
    for s in (-1, 1):
        e = Vector((s * 4.6, 0.4, bz - 0.2))
        rod(P, f"boom{s}", (s * 0.15, 0, bz), e, 0.06, M(HAZ_YEL, wear=0.3), segs=8)
        rod(P, f"stay{s}", (0, 0, 8.65), e.lerp(Vector((0, 0, bz)), 0.35), 0.025, dk, segs=4)
        for k in range(1, 7):
            q = Vector((s * 0.15, 0, bz)).lerp(e, k / 7)
            K.box("nozzle", (0.07, 0.07, 0.18), dk, loc=q - Vector((0, 0, 0.12)), bev=0, parent=P)
            if k in (3, 6):
                mist(P, f"drip{s}{k}", (q.x, q.y, q.z - 0.6), 0.22, 2, rnd, rise=-0.45, strength=1.4)
        K.box(f"tip{s}", (0.22, 0.22, 0.3), M(HAZ_BLK), loc=e, bev=0.02, parent=P)
    K.cyl("beacon_base", 0.16, 0.12, dk, loc=(0, 0, 8.75), segs=10, bev=0.01, parent=P)
    K.cyl("beacon", 0.13, 0.26, K.mat("beacon", HAZ_YEL, rough=0.2, emit=HAZ_YEL, emit_strength=2.5), loc=(0, 0, 8.94), segs=10, bev=0.02, parent=P)
    K.torus("beacon_cage", 0.17, 0.012, dk, loc=(0, 0, 8.94), segs=12, parent=P)
    furrows(P, "scar", (0.0, -2.6, 0.0), 4.0, 3, yaw=10, rnd=rnd)
    return P


def thresher_tractor_graveyard(seed=204):
    """A parts graveyard of torn-up tractors: a Tiller-red chassis with its hood ripped back, a cab shell on its side,
    big lugged tyres dropped and leaned about, a tiller drum and an engine block, soil heaped where they were dragged."""
    rnd = random.Random(seed)
    P = _root("tractor_graveyard")
    K.disc("churned", 4.0, _soil(True), (0, 0), z=0.012, segs=28, wobble=0.35, seed=seed).parent = P
    for k, (x, y) in enumerate(((3.6, -1.2), (-3.9, -0.2))):
        hazard_bands(P, f"stake{k}", (x, y, -0.2), (x + 0.1, y, 1.6), 0.1, 6)
    # the chassis on blocks: rails, engine block, the hood torn up and back
    C = _empty(P, "chassis", (-0.6, -0.4, 0), (0, 0, 12))
    for s in (-1, 1):
        K.box("rail", (4.2, 0.16, 0.3), _dark(), loc=(0, s * 0.5, 0.75), bev=0.02, parent=C)
        K.box("block", (0.4, 0.5, 0.6), K.mat("concrete", CONCRETE, rough=0.9, grime=0.6), loc=(s * 1.4, 0, 0.3), bev=0.03, parent=C)
    K.box("engine", (1.6, 0.9, 0.8), _grey(), loc=(-0.6, 0, 1.3), bev=0.04, parent=C)
    for k in range(4):
        K.cyl("cyl_head", 0.1, 0.25, _dark(), loc=(-1.2 + k * 0.4, 0, 1.8), segs=8, bev=0, parent=C)
    H = _empty(C, "hood", (0.4, 0, 1.5), (0, -52, 0))
    K.box("hood", (2.4, 1.15, 0.08), _till(), loc=(-1.2, 0, 0.5), bev=0.02, parent=H)
    for s in (-1, 1):
        K.box("hood_side", (2.4, 0.06, 0.55), _till(), loc=(-1.2, s * 0.56, 0.25), bev=0.01, parent=H)
    K.box("grille", (0.08, 1.1, 0.75), _cream(), loc=(-2.42, 0, 0.18), bev=0.02, parent=H)
    for k in range(6):
        K.box("grille_bar", (0.1, 0.95, 0.04), _dark(), loc=(-2.47, 0, -0.1 + k * 0.1), bev=0, parent=H)
    K.box("seat_post", (0.1, 0.1, 0.6), _dark(), loc=(1.4, 0, 1.15), bev=0, parent=C)
    K.box("seat", (0.5, 0.5, 0.1), _dark(), loc=(1.4, 0, 1.48), bev=0.03, parent=C)
    K.box("fender", (1.0, 0.1, 0.9), _till(), loc=(1.5, -0.62, 1.1), rot=(0, 0, 0), bev=0.02, parent=C)
    # the cab shell on its side: cream roof, red posts
    S = _empty(P, "cab", (2.7, 1.9, 0), (0, 0, -30))
    K.box("cab_roof", (1.7, 0.12, 1.7), _cream(), loc=(0, -0.9, 0.85), bev=0.03, parent=S)
    for a in (-1, 1):
        for b in (-1, 1):
            K.box("cab_post", (0.1, 1.7, 0.1), _till(), loc=(a * 0.78, 0, 0.85 + b * 0.78), bev=0.01, parent=S)
    K.box("cab_floor", (1.7, 0.1, 1.7), _till(), loc=(0, 0.88, 0.85), bev=0.02, parent=S)
    K.box("cab_glass", (1.5, 1.5, 0.03), K.mat("darkglass", "#22303a", rough=0.15, metal=0.2), loc=(0, 0, 1.62), rot=(0, 0, 0), bev=0, parent=S)
    stripes_y(S, (0, -0.97, 0.3), 1.5, 0.15, n=6)
    # tyres: one leaning on the chassis, one flat, a stack of two, a front wheel
    ag_tyre(P, "lean", (-0.9, -1.75, 0.95), 0.95, 0.55, rot=(0, 0, 8), lugs=16)
    ag_tyre(P, "flat", (-3.3, 1.6, 0.3), 0.95, 0.55, rot=(90, 0, 15), lugs=16)
    ag_tyre(P, "stack1", (1.8, -2.4, 0.25), 0.7, 0.45, rot=(90, 0, 0), lugs=12)
    ag_tyre(P, "stack2", (1.85, -2.35, 0.71), 0.7, 0.45, rot=(84, 0, 40), lugs=12)
    ag_tyre(P, "front", (-2.8, -1.1, 0.5), 0.5, 0.3, rot=(0, 0, 70), lugs=10)
    # the tiller drum: a rotor of L-tines on its shaft, dropped in the dirt
    D = _empty(P, "drum", (0.6, 2.9, 0.4), (0, 0, 5))
    K.cyl("rotor", 0.12, 2.2, _steel(), rot=(0, 90, 0), segs=10, bev=0, parent=D)
    for k in range(7):
        x = -0.95 + k * 0.32
        for j in range(4):
            a = math.radians(90 * j + 25 * k)
            p = Vector((x, math.cos(a) * 0.2, math.sin(a) * 0.2))
            beam(D, "tine", p, p + Vector((0, math.cos(a) * 0.18, math.sin(a) * 0.18)), 0.05, _rusty(), d=0.02, bev=0)
    K.box("drum_guard", (2.3, 0.6, 0.06), _till(), loc=(0, 0.1, 0.42), rot=(20, 0, 0), bev=0.01, parent=D)
    for k in range(6):
        lump(P, f"clod{k}", rnd.uniform(0.15, 0.25), _soil(), (rnd.uniform(-4, 4), rnd.uniform(-3, 3), 0.05), rnd)
    return P


def thresher_warning_stake(seed=205):
    """A warning stake: a hazard-banded post driven into a mound of turned soil, a length of sickle bar bolted across its
    top with the knife guards pointing out, a pesticide canister hung off it on a chain."""
    rnd = random.Random(seed)
    P = _root("warning_stake")
    lump(P, "mound", 0.42, _soil(), (0, 0, 0.0), rnd, flat=0.45)
    hazard_bands(P, "post", (0, 0, -0.2), (0.04, 0.02, 2.2), 0.12, 8)
    G = _empty(P, "bar", (0.04, -0.08, 1.95), (0, 8, 0))
    K.box("bar", (1.4, 0.1, 0.06), _dark(), bev=0, parent=G)
    for k in range(9):
        K.cyl("guard", 0.04, 0.26, _steel(), loc=(-0.64 + k * 0.16, -0.17, 0), rot=(90, 0, 0), segs=4, r2=0.005, bev=0, parent=G)
    for x in (-0.06, 0.06):
        K.cyl("bolt", 0.025, 0.04, _zinc(), loc=(x, -0.07, 0), rot=(90, 0, 0), segs=6, bev=0, parent=G)
    for k in range(4):
        K.torus("link", 0.03, 0.008, _dark(), loc=(0.55, -0.17, -0.05 - k * 0.05), rot=(90 * (k % 2), 0, 0), segs=8, parent=G)
    K.cyl("canister", 0.1, 0.32, _olive(), loc=(0.55, -0.17, -0.42), segs=12, bev=0.01, parent=G)
    K.cyl("canister_cap", 0.05, 0.05, M(HAZ_YEL), loc=(0.55, -0.17, -0.24), segs=8, bev=0, parent=G)
    K.disc("drip", 0.22, _pest(0.5), (0.6, -0.15), z=0.02, segs=12, wobble=0.3, seed=seed).parent = P
    return P


def thresher_tyre_cairn(seed=206):
    """A tyre cairn: three lugged tractor tyres stacked flat, a hazard-banded pole through them with a disc-harrow disc
    impaled on top, the soil round it churned."""
    rnd = random.Random(seed)
    P = _root("tyre_cairn")
    z = 0.0
    for k, (r, w) in enumerate(((0.85, 0.48), (0.75, 0.42), (0.62, 0.36))):
        z += w / 2
        ag_tyre(P, f"tyre{k}", (rnd.uniform(-0.06, 0.06), rnd.uniform(-0.06, 0.06), z), r, w, rot=(90, rnd.uniform(-4, 4), rnd.uniform(0, 90)), lugs=14,
                rim=[_till(), _cream(), _mustard()][k])
        z += w / 2
    hazard_bands(P, "pole", (0, 0, 0.0), (0, 0, 2.3), 0.1, 7)
    # a crown of spring tines torn off a harrow, bolted round the pole's head and curling out
    K.box("clamp", (0.2, 0.2, 0.12), _dark(), loc=(0, 0, 2.05), bev=0.01, parent=P)
    for k in range(6):
        a = 60 * k + 15
        T = _empty(P, f"tine{k}", (0, 0, 2.05), (0, 0, a))
        K.torus(f"tine{k}", 0.24, 0.022, _rusty(), loc=(0.24, 0, 0), rot=(90, 0, 0), arc=200, segs=14, parent=T)
        K.cyl(f"tine_tip{k}", 0.022, 0.18, _rusty(), loc=(0.45, 0, -0.17), rot=(0, 20, 0), segs=6, r2=0.004, bev=0, parent=T)
    K.cyl("cap", 0.09, 0.08, M(HAZ_YEL, wear=0.3), loc=(0, 0, 2.34), segs=10, bev=0.01, parent=P)
    rod(P, "chain", (0.06, -0.06, 1.9), (0.3, -0.3, 1.55), 0.012, _dark(), segs=4)
    K.cyl("canister", 0.1, 0.3, _olive(), loc=(0.32, -0.32, 1.38), segs=12, bev=0.01, parent=P)
    for k in range(5):
        lump(P, f"clod{k}", rnd.uniform(0.12, 0.2), _soil(), (math.cos(k * 1.3) * 1.0, math.sin(k * 1.3) * 1.0, 0.03), rnd)
    return P


def thresher_tilled_scar(seed=207):
    """A tilled scar: a strip of ground a Tiller tore up, turned into ridges and clods, a snapped L-tine still stuck in it
    and pesticide pooled in the trough."""
    rnd = random.Random(seed)
    P = _root("tilled_scar")
    furrows(P, "scar", (0, 0, 0), 3.6, 4, pitch=0.42, yaw=0, h=0.16, rnd=rnd, clods=7)
    for k in range(9):
        lump(P, f"clod{k}", rnd.uniform(0.07, 0.15), _soil(), (rnd.uniform(-2.0, 2.0), rnd.choice((-1, 1)) * rnd.uniform(0.95, 1.2), 0.04), rnd)
    G = _empty(P, "tine", (0.9, 0.2, 0.05), (0, 22, 25))
    K.box("tine_shank", (0.08, 0.03, 0.75), _rusty(), loc=(0, 0, 0.25), bev=0.005, parent=G)
    K.box("tine_blade", (0.36, 0.03, 0.1), _rusty(), loc=(0.16, 0, -0.1), rot=(0, 20, 0), bev=0.005, parent=G)
    K.box("tine_bolt", (0.1, 0.05, 0.1), _dark(), loc=(0, 0, 0.55), bev=0, parent=G)
    K.disc("stain", 0.8, K.mat("spill", SPILL, rough=0.3), (-2.1, -0.5), z=0.02, segs=16, wobble=0.4, seed=seed + 1).parent = P
    K.disc("pool", 0.35, _pest(0.3), (-2.15, -0.5), z=0.026, segs=12, wobble=0.35, seed=seed).parent = P
    return P

PROPS = {
    "spooler_reel_tower": spooler_reel_tower,
    "spooler_tape_web": spooler_tape_web,
    "spooler_crate_bunker": spooler_crate_bunker,
    "spooler_sniper_gantry": spooler_sniper_gantry,
    "spooler_cocoon": spooler_cocoon,
    "spooler_sentinel": spooler_sentinel,
    "spooler_reel_barricade": spooler_reel_barricade,
    "thresher_den": thresher_den,
    "thresher_pesticide_shrine": thresher_pesticide_shrine,
    "thresher_spray_mast": thresher_spray_mast,
    "thresher_tractor_graveyard": thresher_tractor_graveyard,
    "thresher_warning_stake": thresher_warning_stake,
    "thresher_tyre_cairn": thresher_tyre_cairn,
    "thresher_tilled_scar": thresher_tilled_scar,
}
