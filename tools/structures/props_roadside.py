"""Roadside props for the scenes along roads (docs/world-generation.md, Scenes): what the colony put up beside its
roads and what the survivors left along them since, in the Colony Works and Settler Patchwork families of
docs/art-direction.md.

Colony Works: a 4 m steel guard rail (tiles along x) and a section of it bent by a crash, a junction sign of three
blank arrow plates, a curve chevron board, a Grazer-crossing warning triangle, a kilometre milestone, a reflector post,
a wooden-and-steel cable pole whose three wire stubs line up with the next pole's 40 m on, a small faded billboard with
the colony mark, road cones round a hole and crates fallen off a truck. Settler Patchwork: a bus stop bench under a
patched lean-to, a little roadside shrine with its lamp, a hitchhiker's cardboard sign on a crate, a broken-down
scooter, a pile of tyres, a shopping cart left in the grass and the skid marks and debris of a crash. Signs carry
shapes and colours, never words.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front, the road side, towards -Y). They are world objects, so their models carry no collider: the object's data
gives one.
"""
import math
import random

import bmesh
from mathutils import Matrix

import kit as K
import frame as FR
from families import M, glow, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, BRICK, RUST, ZINC, WARM
from props import _root, wood, plank, bolt
from props_settler_a import rod, beam, lump, corrugated, sack, tin, rope_mat, SACK
from props_colony_a import stencil, _concrete, _steel, _dark, _zinc, _empty, pin_insulator
from props_colony_b import plate_xz, _poly, tyre, traffic_cone, rust_patch, TYRE, CARDBOARD
from props_salvage import tyre_ring
from props_camp import crate
from props_fields import Mesh, mat_of, GRASS, WEED

CREOSOTE = "#5e4a38"     # a tarred wooden pole
SCUFF = "#1e1f21"        # rubber laid on the road
GLASS_L = "#9fb3bd"      # broken headlamp glass
SHADOW = "#1a1816"       # a dark opening


# ---------------------------------------------------------------- helpers
def _grass(P, rnd, spots, h=0.3, n=5, name="grass"):
    """Grass tufts at the given (x, y) spots, the way it grows up round anything left by a road."""
    C = Mesh()
    g = mat_of("tgrass", GRASS, rough=0.85, noise_scale=6.0)
    w = mat_of("weed", WEED, rough=0.85, noise_scale=6.0)
    for k, (x, y) in enumerate(spots):
        C.tuft((x, y, 0.0), rnd, g if k % 3 else w, n=n, h=h * rnd.uniform(0.8, 1.2))
    return C.build(P, name)


W_PROF = [(0.0, 0.0), (-0.025, 0.025), (-0.085, 0.07), (-0.085, 0.11), (-0.035, 0.145), (-0.035, 0.165), (-0.085, 0.2),
          (-0.085, 0.24), (-0.025, 0.285), (0.0, 0.31)]


def w_beam(P, name, xs, mat, bend=None, t=0.006):
    """A pressed-steel W-beam rail 0.31 m deep along x through the stations xs, its back at y = 0 and its two ribs
    bulging towards -y, its foot at z = 0. bend(x) -> (dy, dz, tilt, squash) pushes it back, drops it, tips its top
    back (degrees) and flattens its ribs, for a crash."""
    bm = bmesh.new()
    rings = []
    for x in xs:
        dy, dz, tilt, sq = bend(x) if bend else (0.0, 0.0, 0.0, 1.0)
        ca, sa = math.cos(math.radians(tilt)), math.sin(math.radians(tilt))
        loop = [(y * sq, z) for y, z in W_PROF] + [(y * sq + t, z) for y, z in reversed(W_PROF)]
        ring = []
        for y, z in loop:
            zc = z - 0.155
            yr, zr = y * ca + zc * sa, -y * sa + zc * ca
            ring.append(bm.verts.new((x, yr + dy, zr + 0.155 + dz)))
        rings.append(ring)
    n = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for j in range(n):
            bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.data.set_sharp_from_angle(angle=math.radians(40))
    return K.place(ob, parent=P)


def _galv(**kw):
    kw.setdefault("metal", 0.55)
    kw.setdefault("wear", 0.2)
    kw.setdefault("grime", 0.45)
    kw.setdefault("rough", 0.5)
    return M(ZINC, **kw)


def _button(P, loc, r=0.016):
    """A round-headed splice bolt on the face of the rail."""
    K.cyl("button", r, 0.012, _galv(wear=0.1), loc=loc, rot=(90, 0, 0), segs=8, bev=0.004, parent=P)


def rail_post(P, name, x, h=1.0, lean=(0.0, 0.0), bent=None):
    """A C-section steel guard-rail post at x, open side back, sunk 0.25 m; bent (z, degrees) folds it back above z."""
    G = _empty(P, name, (x, 0.04, -0.25), (lean[0], lean[1], 0))
    m = _galv(wear=0.25, grime=0.55)
    H = h + 0.25
    z_b, ang = bent if bent else (H, 0.0)
    for part, z0, z1, parent in (("lo", 0.0, min(z_b, H), G), ("hi", 0.0, H - z_b, None)):
        if part == "hi":
            if not bent:
                break
            parent = _empty(G, f"{name}_fold", (0, 0, z_b), (-ang, 0, 0))
        L = z1 - z0
        K.box(f"{name}_web_{part}", (0.15, 0.008, L), m, loc=(0, -0.05, z0 + L / 2), bev=0.002, parent=parent)
        for s in (-1, 1):
            K.box(f"{name}_flange_{part}", (0.008, 0.1, L), m, loc=(s * 0.071, 0.0, z0 + L / 2), bev=0.002, parent=parent)
    return G


# ================================================================== Colony Works road furniture
def guard_rail(seed=41):
    """A 4 m run of colony guard rail along x (tiles along x): a galvanised W-beam on spacer blocks and C-section posts
    every 2 m, spliced in the middle with eight round-headed bolts, a mustard reflector on one post, rust weeping from
    the bolts and grass at the post feet."""
    rnd = random.Random(seed)
    P = _root("guardrail")
    z0, yb = 0.52, -0.16
    R = _empty(P, "rail", (0, yb, z0))
    w_beam(R, "beam_a", [-2.0, 0.17], _galv())
    w_beam(R, "beam_b", [-0.17, 2.0], _galv()).location = (0, -0.007, 0)
    for x in (-0.1, 0.1):
        for z in (0.09, 0.22):
            _button(R, (x, -0.048, z))
    for x in (-1.0, 1.0):
        rail_post(P, f"post{x}", x)
        K.box(f"spacer{x}", (0.15, 0.13, 0.36), _galv(wear=0.3, grime=0.5), loc=(x, -0.08, z0 + 0.15), bev=0.004, parent=P)
        _button(R, (x, -0.048, 0.155))
        K.box(f"cap{x}", (0.16, 0.11, 0.01), _galv(), loc=(x, 0.04, 0.755), bev=0.002, parent=P)
        rust_patch(R, (x + 0.02, -0.086, 0.1), (0.03, 0.004, 0.09))
    # a mustard reflector on the post at x = 1 (one per section, so the run keeps its rhythm)
    K.box("refl_arm", (0.03, 0.03, 0.16), _galv(), loc=(1.0, 0.04, 0.82), bev=0.003, parent=P)
    K.box("refl", (0.1, 0.012, 0.07), M(MUSTARD, wear=0.3, grime=0.3, rough=0.25), loc=(1.0, 0.02, 0.88), bev=0.004, parent=P)
    rust_patch(R, (-0.07, -0.087, 0.18), (0.04, 0.004, 0.12))
    _grass(P, rnd, [(-1.0, 0.1), (-0.9, -0.1), (1.0, 0.12), (1.1, -0.08), (0.3, 0.2), (-1.6, 0.15)], h=0.32)
    return P


def guard_rail_bent(seed=42):
    """A 4 m section of guard rail (its left end meets a straight run) hit hard by something: the beam folded back
    and down round the impact, its ribs crushed flat and its top tipped back, the right post bent over, the beam torn
    free past it and dragging in the grass; paint scraped onto it, a spacer block thrown, headlamp glass and a tyre
    scuff in front."""
    rnd = random.Random(seed)
    P = _root("guardrail_bent")
    z0, yb = 0.52, -0.16
    R = _empty(P, "rail", (0, yb, z0))

    def bend(x):
        f = math.exp(-((x - 0.5) / 0.85) ** 2)
        tail = max(0.0, x - 1.1)
        return (0.75 * f + 0.4 * tail, -0.18 * f - 0.42 * tail ** 1.2, 38 * f + 25 * min(tail, 0.6), 1 - 0.65 * f)

    xs = [-2.0, -1.5, -1.1, -0.8, -0.5, -0.25, 0.0, 0.2, 0.4, 0.6, 0.8, 1.0, 1.2, 1.4, 1.6, 1.8]
    w_beam(R, "beam", xs, _galv(wear=0.3), bend=bend)
    # paint transfer and scrapes on the crushed face
    for k, (x, z) in enumerate(((0.1, 0.05), (0.5, -0.02), (0.75, -0.05))):
        dy, dz, tilt, sq = bend(x)
        K.box(f"paint{k}", (0.32, 0.01, 0.05), M(SIGNAL, wear=0.4, grime=0.3), loc=(x, dy - 0.04 * sq - 0.004, 0.15 + dz + z),
              rot=(-tilt, 0, rnd.uniform(-8, 8)), bev=0, parent=R)
    rail_post(P, "post_l", -1.0, lean=(0, 3))
    K.box("spacer_l", (0.15, 0.13, 0.36), _galv(wear=0.3, grime=0.5), loc=(-1.0, -0.08, z0 + 0.15), bev=0.004, parent=P)
    _button(R, (-1.0, -0.048, 0.155))
    rail_post(P, "post_r", 1.0, lean=(-8, 0), bent=(0.4, 62))
    # the thrown spacer block in the grass and the torn bolt hole's tag of steel
    K.box("spacer_thrown", (0.15, 0.13, 0.36), _galv(wear=0.4, grime=0.6), loc=(0.2, 0.9, 0.065), rot=(90, 0, 35), bev=0.004, parent=P)
    rust_patch(R, (-1.05, -0.087, 0.1), (0.03, 0.004, 0.09))
    # debris on the road side: headlamp glass and a black scuff where the tyre slid
    for k in range(9):
        s = rnd.uniform(0.04, 0.09)
        pts = [(0, 0), (s, rnd.uniform(-0.02, 0.02)), (rnd.uniform(0.2, 0.8) * s, s * 0.8)]
        plate_xz(f"shard{k}", pts, 0.006, K.mat("lampglass", GLASS_L, rough=0.1, wear=0.2), loc=(rnd.uniform(-0.6, 1.2), rnd.uniform(-1.0, -0.4), 0.004),
                 rot=(90, 0, rnd.uniform(0, 360)), parent=P, bev=0)
    sc = K.strip("scuff", [(-1.8, -1.35), (-0.8, -1.0), (0.0, -0.6), (0.4, -0.38)], 0.2, K.mat("scuff", SCUFF, rough=0.9), z=0.006)
    sc.parent = P
    _grass(P, rnd, [(-1.0, 0.1), (-0.9, -0.1), (1.0, 0.25), (1.6, 0.3), (-1.6, 0.15), (0.6, 0.6)], h=0.32)
    return P


def _arrow(w, h, tip):
    """An arrow plate outline pointing +x: w long, h high, the point tip long."""
    return [(0, -h / 2), (w - tip, -h / 2), (w, 0), (w - tip, h / 2), (0, h / 2)]


def _inset(pts, d):
    """pts pulled d towards their centre (a border inside a plate)."""
    cx = sum(p[0] for p in pts) / len(pts)
    cz = sum(p[1] for p in pts) / len(pts)
    out = []
    for x, z in pts:
        vx, vz = x - cx, z - cz
        L = math.hypot(vx, vz) or 1
        out.append((x - vx / L * d, z - vz / L * d))
    return out


def _post_base(P, x=0.0, y=0.0, r=0.2):
    K.cyl("collar", r, 0.12, _concrete(0.3), loc=(x, y, 0.03), segs=10, bev=0.02, parent=P)


def road_sign_junction(seed=43):
    """A colony junction sign, 3 m: a steel-blue square post on a concrete collar with three blank arrow plates in the
    colony colours (mustard, steel blue, bone) pointing left, right and half-right, each with its border, clamps and
    galvanised back. No words: the colours stand for the roads."""
    rnd = random.Random(seed)
    P = _root("junction")
    _post_base(P)
    post_m = M(STEEL, metal=0.35, wear=0.45, grime=0.4)
    K.box("post", (0.08, 0.08, 3.2), post_m, loc=(0, 0, 1.4), bev=0.01, parent=P)
    K.box("post_cap", (0.1, 0.1, 0.03), _dark(), loc=(0, 0, 3.0), bev=0.006, parent=P)
    K.cyl("finial", 0.03, 0.06, _dark(), loc=(0, 0, 3.05), segs=8, r2=0.012, bev=0.004, parent=P)
    plates = [(2.68, -1, 0, MUSTARD, GRAPHITE), (2.3, 1, 0, STEEL, BONE), (1.92, 1, 28, BONE, SIGNAL)]
    for k, (z, s, yaw, face, edge) in enumerate(plates):
        G = _empty(P, f"plate{k}", (0, 0, z), (0, 0, yaw))
        w, h, tip = 1.0, 0.28, 0.16
        pts = _arrow(w, h, tip)
        if s < 0:
            pts = [(-x, zz) for x, zz in pts]
        off = 0.06 * s
        plate_xz("back", pts, 0.012, _galv(wear=0.4), loc=(off, 0.0, 0), parent=G, bev=0.003)
        plate_xz("edge", _inset(pts, 0.005), 0.008, M(edge, wear=0.4, grime=0.35, rough=0.5), loc=(off, -0.009, 0), parent=G, bev=0.002)
        plate_xz("face", _inset(pts, 0.03), 0.008, M(face, wear=0.5, grime=0.35, rough=0.5, scratches=0.4), loc=(off, -0.013, 0), parent=G, bev=0.002)
        plate_xz("face_b", _inset(pts, 0.03), 0.008, M(face, wear=0.6, grime=0.45, rough=0.5), loc=(off, 0.009, 0), parent=G, bev=0.002)
        for dx in (0.12, 0.62):
            K.box("clamp", (0.04, 0.03, h + 0.03), _galv(), loc=(off + s * dx, 0.02, 0), bev=0.004, parent=G)
            bolt(G, (off + s * dx, -0.02, h / 2 - 0.04))
        K.box("collar", (0.11, 0.11, 0.09), _dark(), loc=(0, 0, 0), bev=0.008, parent=G)
    rust_patch(P, (0.0, -0.041, 0.6), (0.05, 0.004, 0.12))
    _grass(P, rnd, [(0.15, 0.1), (-0.2, 0.05), (0.05, -0.2)], h=0.3)
    return P


def road_sign_warning_curve(seed=44):
    """A colony curve warning: a graphite board 1.2 m wide with three mustard chevrons pointing right in a bone frame,
    on two steel posts, a reflector strip along its foot."""
    rnd = random.Random(seed)
    P = _root("chevron")
    W, H, zc = 1.2, 0.6, 1.25
    post_m = M(STEEL, metal=0.35, wear=0.45, grime=0.4)
    for s in (-1, 1):
        K.box(f"post{s}", (0.06, 0.06, zc + 0.5), post_m, loc=(s * 0.4, 0.03, (zc + 0.5) / 2 - 0.15), bev=0.008, parent=P)
        K.box(f"post_cap{s}", (0.07, 0.07, 0.02), _dark(), loc=(s * 0.4, 0.03, zc + 0.36), bev=0.004, parent=P)
        _post_base(P, s * 0.4, 0.03, 0.12)
    rect = [(-W / 2, -H / 2), (W / 2, -H / 2), (W / 2, H / 2), (-W / 2, H / 2)]
    plate_xz("back", rect, 0.015, _galv(wear=0.4), loc=(0, 0.004, zc), parent=P, bev=0.004)
    plate_xz("frame", _inset(rect, 0.004), 0.01, M(BONE, wear=0.4, grime=0.4, rough=0.5), loc=(0, -0.008, zc), parent=P, bev=0.002)
    plate_xz("face", [(-W / 2 + 0.035, -H / 2 + 0.035), (W / 2 - 0.035, -H / 2 + 0.035), (W / 2 - 0.035, H / 2 - 0.035), (-W / 2 + 0.035, H / 2 - 0.035)],
             0.01, M(GRAPHITE, wear=0.3, grime=0.3, rough=0.5), loc=(0, -0.013, zc), parent=P, bev=0.002)
    ink = M(MUSTARD, wear=0.5, grime=0.3, rough=0.45, scratches=0.4)
    for k in range(3):
        x = -0.32 + k * 0.32
        a, b, d = 0.13, 0.2, 0.09
        pts = [(x - a, b), (x - a + d, b), (x + a, 0), (x - a + d, -b), (x - a, -b), (x + a - d, 0)]
        plate_xz(f"chev{k}", pts, 0.006, ink, loc=(0, -0.02, zc), parent=P, bev=0)
    for s in (-1, 1):
        for dz in (-0.18, 0.18):
            K.box("clamp", (0.09, 0.025, 0.035), _galv(), loc=(s * 0.4, 0.06, zc + dz), bev=0.004, parent=P)
            bolt(P, (s * 0.4, -0.02, zc + dz))
    K.box("reflector", (W - 0.1, 0.012, 0.04), M(BONE, rough=0.2, wear=0.2), loc=(0, -0.016, zc - H / 2 - 0.04), bev=0.003, parent=P)
    _grass(P, rnd, [(-0.4, 0.1), (0.4, 0.0), (0.0, 0.15)], h=0.3)
    return P


def _ellipse(cx, cz, rx, rz, n=14, a0=0.0, a1=2 * math.pi):
    return [(cx + rx * math.cos(a0 + (a1 - a0) * k / n), cz + rz * math.sin(a0 + (a1 - a0) * k / n)) for k in range(n)]


def grazer_pictogram(G, y, ink, s=1.0):
    """The Grazer in silhouette facing left: a round heavy body on short legs, a big round head with a long snout, a
    droopy ear, a curled horn and a tufted tail, as flat plates at depth y in the xz plane of G."""
    def sp(pts):
        return [(x * s, z * s) for x, z in pts]

    parts = [
        _ellipse(0.04, 0.0, 0.17, 0.105, 16),                       # body
        _ellipse(-0.15, 0.06, 0.088, 0.085, 14),                    # head
        _ellipse(-0.235, 0.025, 0.065, 0.042, 12),                  # snout
        [(-0.14, -0.02), (-0.07, -0.04), (-0.05, 0.06), (-0.12, 0.08)],   # neck
        [(-0.12, 0.11), (-0.065, 0.12), (-0.06, 0.04), (-0.085, 0.03)],    # ear, drooping back
        [(0.19, 0.04), (0.2, 0.02), (0.255, -0.06), (0.24, -0.07)],        # tail
        _ellipse(0.248, -0.075, 0.02, 0.026, 8),                    # tail tuft
    ]
    for x in (-0.085, -0.035, 0.1, 0.15):
        parts.append([(x - 0.02, -0.2), (x + 0.02, -0.2), (x + 0.022, -0.06), (x - 0.022, -0.06)])
    for k, pts in enumerate(parts):
        plate_xz(f"grazer{k}", sp(pts), 0.004, ink, loc=(0, y, 0), parent=G, bev=0)
    # the horn: a fleshy curl rising off the head
    h = K.torus("horn", 0.028 * s, 0.011 * s, ink, loc=(-0.155 * s, y, 0.16 * s), rot=(90, 0, 0), arc=290, segs=14, parent=G)
    h.scale = (1, 1, 0.35)


def road_sign_animal(seed=45):
    """A colony animal-crossing warning: a mustard triangle in a graphite border with a Grazer in silhouette, on a
    steel post. No words."""
    rnd = random.Random(seed)
    P = _root("animal_sign")
    post_m = M(STEEL, metal=0.4, wear=0.6, grime=0.4)
    K.box("post", (0.06, 0.06, 2.3), post_m, loc=(0, 0.04, 1.0), bev=0.008, parent=P)
    K.box("post_cap", (0.07, 0.07, 0.02), post_m, loc=(0, 0.04, 2.15), bev=0.004, parent=P)
    _post_base(P, 0, 0.04, 0.14)
    zc = 1.92
    tri = lambda r: _poly(3, r, math.pi / 2)  # noqa: E731
    plate_xz("back", tri(0.52), 0.015, _galv(wear=0.4), loc=(0, 0.004, zc), parent=P, bev=0.003)
    plate_xz("border", tri(0.51), 0.012, M(GRAPHITE, wear=0.3, grime=0.3, rough=0.5), loc=(0, -0.01, zc), parent=P, bev=0.002)
    plate_xz("face", tri(0.42), 0.012, M(MUSTARD, wear=0.5, grime=0.4, rough=0.5, scratches=0.4), loc=(0, -0.016, zc), parent=P, bev=0.002)
    G = _empty(P, "picto", (0.0, 0, zc - 0.03))
    grazer_pictogram(G, -0.024, M(GRAPHITE, wear=0.15, scratches=0), s=0.92)
    for dz in (-0.2, 0.16):
        K.box("clamp", (0.26, 0.025, 0.04), _galv(), loc=(0, 0.075, zc + dz), bev=0.005, parent=P)
        bolt(P, (0, -0.012, zc + dz))
    _grass(P, rnd, [(0.15, 0.1), (-0.15, 0.05), (0.05, -0.15)], h=0.3)
    return P


def km_marker(seed=46):
    """A colony kilometre stone: a pill-cornered concrete post 0.8 m tall, its head painted bone with a mustard cap
    band, the kilometre stencilled in graphite on the road face, a reflector stud, standing in a little kerbed pad."""
    rnd = random.Random(seed)
    P = _root("km")
    K.box("pad", (0.5, 0.4, 0.08), _concrete(0.4), loc=(0, 0, 0.01), bev=0.02, parent=P)
    G = _empty(P, "lean", (0, 0, 0.0), (rnd.uniform(-2, 2), rnd.uniform(-3, 3), rnd.uniform(-3, 3)))
    K.slab("body", 0.3, 0.2, 0.86, 0.07, _concrete(0.3), loc=(0, 0, 0.36), bev=0.02, parent=G)
    K.slab("paint", 0.31, 0.21, 0.36, 0.07, M(BONE, wear=0.45, grime=0.4, rough=0.7), loc=(0, 0, 0.6), bev=0.02, parent=G)
    K.slab("cap", 0.315, 0.215, 0.07, 0.07, M(MUSTARD, wear=0.55, grime=0.35, rough=0.6), loc=(0, 0, 0.77), bev=0.015, parent=G)
    K.box("band", (0.312, 0.212, 0.03), M(GRAPHITE, wear=0.3), loc=(0, 0, 0.43), bev=0.004, parent=G)
    stencil(G, "12", (0, -0.107, 0.6), 0.14, M(GRAPHITE, wear=0.15, scratches=0), extrude=0.003)
    K.cyl("stud", 0.022, 0.012, M(MUSTARD, rough=0.15, wear=0.1), loc=(0, -0.106, 0.33), rot=(90, 0, 0), segs=10, bev=0.003, parent=G)
    _grass(P, rnd, [(0.25, 0.15), (-0.25, 0.1), (0.2, -0.2)], h=0.25)
    return P


def road_reflector_post(seed=47):
    """A colony delineator post 1.1 m tall: a bone plastic post of rounded section, a graphite band near its top with a
    mustard reflector to the road and a smaller bone one behind, leaning a little where a mower clipped it."""
    rnd = random.Random(seed)
    P = _root("delineator")
    G = _empty(P, "lean", (0, 0, 0), (-4, 2.5, 0))
    plastic = M(BONE, wear=0.25, grime=0.55, rough=0.55, scratches=0.4)
    K.slab("post", 0.11, 0.07, 1.25, 0.03, plastic, loc=(0, 0, 0.475), bev=0.012, parent=G)
    K.slab("band", 0.114, 0.074, 0.22, 0.03, M(GRAPHITE, wear=0.2, rough=0.5), loc=(0, 0, 0.92), bev=0.008, parent=G)
    K.box("refl", (0.05, 0.008, 0.14), M(MUSTARD, rough=0.15, wear=0.1), loc=(0, -0.038, 0.92), bev=0.003, parent=G)
    K.box("refl_back", (0.03, 0.008, 0.05), M(BONE, rough=0.15, wear=0.1), loc=(0, 0.038, 0.95), bev=0.002, parent=G)
    _grass(P, rnd, [(0.06, 0.04), (-0.08, 0.02), (0.0, -0.08), (0.1, -0.05)], h=0.3)
    return P


def colony_cable_pole(seed=48):
    """A colony line pole, 8.5 m: a tarred timber pole in a steel foot sleeve, a steel channel cross arm on raked
    braces carrying three glazed insulators at one height, and from each a 1 m stub of line out along x both ways, so
    poles set about 40 m apart down a road read as one line from afar. Hazard bands, a number plate and step bolts."""
    rnd = random.Random(seed)
    P = _root("cablepole")
    K.cyl("collar", 0.36, 0.12, _concrete(0.4), loc=(0, 0, 0.03), segs=14, bev=0.025, parent=P)
    L = _empty(P, "lean", (0, 0, 0), (rnd.uniform(-1.2, 1.2), rnd.uniform(-1.0, 1.0), 0))
    H = 8.5
    pole = K.mat("creosote", CREOSOTE, rough=0.9, grime=0.35, noise_scale=6.0, cracks=0.4)
    K.cyl("pole", 0.15, H + 0.3, pole, loc=(0, 0, (H + 0.3) / 2 - 0.3), segs=12, r2=0.12, bev=0.015, parent=L)

    def rad(z):
        return 0.15 - 0.03 * (z + 0.3) / (H + 0.3)

    K.cyl("sleeve", 0.172, 1.0, _steel(), loc=(0, 0, 0.42), segs=12, bev=0.012, parent=L)
    for z in (0.15, 0.85):
        K.cyl("sleeve_band", 0.18, 0.05, _dark(), loc=(0, 0, z), segs=12, bev=0.008, parent=L)
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            K.cyl("sleeve_bolt", 0.02, 0.03, _zinc(), loc=(math.cos(a) * 0.19, math.sin(a) * 0.19, z), rot=(0, 90, math.degrees(a)), segs=6, bev=0, parent=L)
    for k in range(4):
        z = 1.1 + k * 0.14
        K.cyl("band", rad(z) + 0.006, 0.14, M(MUSTARD if k % 2 == 0 else GRAPHITE, wear=0.55), loc=(0, 0, z), segs=12, bev=0, parent=L)
    K.box("num_plate", (0.24, 0.02, 0.12), M(BONE, wear=0.55), loc=(0, -rad(1.95) - 0.012, 1.95), bev=0.004, parent=L)
    stencil(L, "L-38", (0, -rad(1.95) - 0.024, 1.95), 0.065, _dark(), extrude=0.004)
    for k in range(9):
        z = 2.4 + k * 0.5
        s = 1 if k % 2 else -1
        a = math.radians(s * 60 + 90)
        r = rad(z)
        K.cyl("step", 0.016, 0.2, _zinc(), loc=(math.cos(a) * (r + 0.09), math.sin(a) * (r + 0.09), z), rot=(90, 0, s * 60), segs=6, bev=0, parent=L)
    # the cross arm across the line (along y), insulators on top at one height, line stubs along x
    za = 7.9
    K.box("arm", (0.1, 2.3, 0.12), _steel(), loc=(rad(za) + 0.05, 0, za), bev=0.012, parent=L)
    K.box("arm_clamp", (0.28, 0.24, 0.2), _dark(), loc=(0.04, 0, za), bev=0.02, parent=L)
    K.cyl("thru_bolt", 0.018, 0.4, _zinc(), loc=(0.05, 0, za), rot=(0, 90, 0), segs=6, bev=0, parent=L)
    for s in (-1, 1):
        beam(L, f"brace{s}", (rad(za) + 0.05, s * 0.75, za - 0.05), (rad(za - 0.7) + 0.02, 0.0, za - 0.7), 0.05, 0.025, _zinc())
    K.box("brace_clamp", (0.06, 0.24, 0.06), _dark(), loc=(rad(za - 0.7) + 0.02, 0, za - 0.7), bev=0.008, parent=L)
    wire = K.mat("line", "#2b2d30", rough=0.55, metal=0.3)
    xa = rad(za) + 0.05
    for y in (-0.95, -0.35, 0.95):
        pin_insulator(L, (xa, y, za + 0.06))
        zt = za + 0.06 + 0.3
        K.torus("tie", 0.05, 0.008, wire, loc=(xa, y, zt), rot=(0, 90, 0), segs=10, parent=L)
        for d in (-1, 1):
            rod(L, f"stub{y}{d}", (xa, y, zt), (xa + d * 1.0, y, zt - 0.05), 0.011, wire, segs=6)
    K.cyl("cap", 0.13, 0.05, _dark(), loc=(0, 0, H + 0.02), segs=12, bev=0.012, parent=L)
    # earth wire stapled down the pole
    K.cyl("earth", 0.008, za - 0.4, K.mat("copper_w", "#8a6a48", rough=0.5, metal=0.5), loc=(-rad(4) - 0.006, 0.0, (za - 0.4) / 2), segs=5, bev=0, parent=L)
    _grass(P, rnd, [(0.3, 0.2), (-0.3, 0.15), (0.1, -0.35), (-0.25, -0.3)], h=0.35)
    return P


def roadside_billboard_small(seed=49):
    """A small colony billboard, a 3 x 1.8 m panel on two steel legs: faded bone, the colony mark on it (a mustard
    ring round a steel-blue chevron over three graphite bars), a corner of the sheet peeled and curling, a broken lamp
    arm over the top and a ribbed galvanised back."""
    rnd = random.Random(seed)
    P = _root("billboard_s")
    W, Hb, z0 = 3.0, 1.8, 1.5
    steel = _steel()
    for s in (-1, 1):
        x = s * 1.0
        K.box(f"footing{s}", (0.45, 0.45, 0.3), _concrete(0.3), loc=(x, 0.1, 0.05), bev=0.03, parent=P)
        K.box(f"leg{s}", (0.12, 0.12, z0 + Hb - 0.1), steel, loc=(x, 0.1, (z0 + Hb - 0.1) / 2 + 0.1), bev=0.012, parent=P)
        K.box(f"leg_plate{s}", (0.24, 0.24, 0.03), _dark(), loc=(x, 0.1, 0.215), bev=0.006, parent=P)
        beam(P, f"knee{s}", (x, 0.12, z0 - 0.6), (x + s * 0.5, 0.06, z0 + 0.02), 0.05, 0.05, _zinc())
    # frame and back
    K.box("back", (W, 0.03, Hb), _galv(wear=0.3), loc=(0, 0.04, z0 + Hb / 2), bev=0.006, parent=P)
    for k in range(5):
        K.box("rib", (0.04, 0.05, Hb - 0.1), _galv(), loc=(-W / 2 + 0.2 + k * (W - 0.4) / 4, 0.075, z0 + Hb / 2), bev=0.004, parent=P)
    fr = M(STEEL, metal=0.3, wear=0.55, grime=0.45)
    for z in (z0, z0 + Hb):
        K.box("frame_h", (W + 0.08, 0.08, 0.08), fr, loc=(0, 0.0, z), bev=0.012, parent=P)
    for x in (-W / 2, W / 2):
        K.box("frame_v", (0.08, 0.08, Hb + 0.08), fr, loc=(x, 0.0, z0 + Hb / 2), bev=0.012, parent=P)
    face = M(BONE, wear=0.75, grime=0.5, scratches=0.7, rough=0.7)
    peel = [(-W / 2 + 0.04, z0 + 0.04), (W / 2 - 0.04, z0 + 0.04), (W / 2 - 0.04, z0 + Hb - 0.5), (W / 2 - 0.5, z0 + Hb - 0.04), (-W / 2 + 0.04, z0 + Hb - 0.04)]
    plate_xz("face", peel, 0.01, face, loc=(0, -0.02, 0), parent=P, bev=0.002)
    # the peeled corner, curling away from the board, the bare galvanised sheet behind it
    plate_xz("bare", [(W / 2 - 0.5, z0 + Hb - 0.04), (W / 2 - 0.04, z0 + Hb - 0.04), (W / 2 - 0.04, z0 + Hb - 0.5)], 0.006, _galv(wear=0.2),
             loc=(0, -0.019, 0), parent=P, bev=0)
    C = _empty(P, "curl", (W / 2 - 0.27, -0.03, z0 + Hb - 0.27), (0, 0, 0))
    for k in range(4):
        a = 25 + k * 30
        K.box(f"curl{k}", (0.12, 0.006, 0.1), face, loc=(0.0 + k * 0.03, -0.03 - k * 0.04, 0.0 + k * 0.03), rot=(0, -45, -a), bev=0, parent=C)
    # the colony mark, faded
    cx, cz = -0.55, z0 + Hb / 2 + 0.08
    ring = K.torus("mark_ring", 0.48, 0.06, M(MUSTARD, wear=0.75, grime=0.4, scratches=0.6), loc=(cx, -0.028, cz), rot=(90, 0, 0), segs=36, parent=P)
    ring.scale = (1, 1, 0.12)
    chev = [(cx - 0.3, cz - 0.22), (cx, cz + 0.28), (cx + 0.3, cz - 0.22), (cx + 0.16, cz - 0.22), (cx, cz + 0.05), (cx - 0.16, cz - 0.22)]
    plate_xz("mark_chev", chev, 0.006, M(STEEL, wear=0.75, grime=0.4, scratches=0.6), loc=(0, -0.027, 0), parent=P, bev=0)
    for k in range(3):
        z = z0 + 0.28 + k * 0.16
        K.box(f"bar{k}", (1.25 - k * 0.25, 0.006, 0.08), M(GRAPHITE, wear=0.7, grime=0.4, scratches=0.6), loc=(0.62 + k * 0.125, -0.027, z + 0.55), bev=0, parent=P)
    K.box("stripe", (W - 0.1, 0.006, 0.07), M(SIGNAL, wear=0.75, grime=0.4, scratches=0.6), loc=(0, -0.027, z0 + 0.14), bev=0, parent=P)
    # the lamp arm, its head knocked askew
    K.box("lamp_arm", (0.04, 0.5, 0.04), _dark(), loc=(0.6, -0.22, z0 + Hb + 0.08), bev=0.006, parent=P)
    K.box("lamp_post", (0.04, 0.04, 0.14), _dark(), loc=(0.6, 0.02, z0 + Hb + 0.02), bev=0.006, parent=P)
    K.slab("lamp_head", 0.26, 0.12, 0.06, 0.03, M(BONE, wear=0.6, grime=0.5), loc=(0.64, -0.48, z0 + Hb + 0.02), rot=(0, 0, 25), bev=0.01, parent=P)
    for k in range(3):
        rust_patch(P, (-1.0 + k * 0.9, 0.06, z0 + 0.05), (0.15, 0.004, 0.05))
    _grass(P, rnd, [(-1.0, -0.1), (1.0, -0.1), (-0.8, 0.3), (1.2, 0.3), (0.0, 0.2)], h=0.35)
    return P


# ================================================================== Settler Patchwork life along the road
def bus_stop_bench_settler(seed=50):
    """A settler bus stop: a bench of two salvaged planks on cinder blocks under a lean-to of patched corrugated sheet
    on scrap poles, a hand-painted bus on a board nailed to the corner pole (a bus shape, no words), a back of odd
    boards and a tin can of flowers."""
    rnd = random.Random(seed)
    P = _root("busstop")
    W, D = 2.4, 1.2
    block = K.mat("cinder", "#8e8b82", rough=0.95, grime=0.5, cracks=0.4, moss=0.2)
    for s in (-1, 1):
        for k in range(2):
            K.box(f"block{s}{k}", (0.2, 0.4, 0.2), block, loc=(s * 0.85, 0.15, 0.1 + k * 0.2), rot=(0, 0, rnd.uniform(-4, 4)), bev=0.01, parent=P)
            K.box(f"hole{s}{k}", (0.21, 0.12, 0.1), K.mat("hole", SHADOW, rough=1.0), loc=(s * 0.85, 0.15, 0.1 + k * 0.2), bev=0, parent=P)
    plank(P, "seat_a", (2.2, 0.22, 0.05), (0, 0.08, 0.425), rot=(0, 0.6, 0), shade=0)
    plank(P, "seat_b", (2.1, 0.2, 0.05), (0.05, 0.3, 0.425), rot=(0, -0.4, 1), shade=2)
    # four scrap poles: two steel pipes in front, two timbers behind, taller, so the roof falls to the road
    hf, hb = 2.15, 2.5
    for s in (-1, 1):
        rod(P, f"pole_f{s}", (s * 1.15, -0.4, -0.2), (s * 1.15, -0.4, hf), 0.035, M(RUST, metal=0.4, wear=0.3, grime=0.5), segs=10)
        K.box(f"pole_b{s}", (0.1, 0.1, hb + 0.2), wood(3, wear=0.5), loc=(s * 1.15, 0.6, hb / 2 - 0.1), bev=0.012, parent=P)
    for y, z in ((-0.4, hf), (0.6, hb)):
        K.box(f"purlin{y}", (W + 0.2, 0.08, 0.1), wood(1, wear=0.5), loc=(0, y, z), bev=0.01, parent=P)
    slope = math.degrees(math.atan2(hb - hf, 1.0))
    for k, (x, col) in enumerate(((-0.75, ZINC), (0.0, SEA), (0.75, ZINC))):
        corrugated(P, f"roof{k}", 0.85, 1.55, tin(col, wear=0.25, grime=0.45), loc=(x, 0.1, (hf + hb) / 2 + 0.07), rot=(-slope, rnd.uniform(-1, 1), 0))
    K.box("roof_patch", (0.4, 0.5, 0.01), tin(BRICK, wear=0.2), loc=(0.3, -0.1, (hf + hb) / 2 + 0.06), rot=(-slope, 0, 8), bev=0.003, parent=P)
    # the back: odd boards between the rear poles, one missing
    for k in range(6):
        if k == 4:
            continue
        z = 0.55 + k * 0.24
        plank(P, f"back{k}", (2.3, 0.025, 0.2), (0, 0.66, z), rot=(0, rnd.uniform(-1.5, 1.5), 0), shade=k)
    plank(P, "back_odd", (0.9, 0.03, 0.22), (-0.5, 0.64, 0.55 + 4 * 0.24), rot=(0, 3, 0), shade=1)
    # the bus board: a hand-painted bus on a weathered board on the front-right pole
    G = _empty(P, "busboard", (1.15, -0.45, 1.75), (0, 0, -12))
    plank(G, "board", (0.62, 0.03, 0.36), (0, -0.04, 0), shade=2)
    paint = M(SEA, wear=0.4, grime=0.3, rough=0.7)
    plate_xz("bus", [(-0.24, -0.08), (0.24, -0.08), (0.24, 0.08), (0.2, 0.12), (-0.24, 0.12)], 0.004, paint, loc=(0, -0.058, 0), parent=G, bev=0)
    for k in range(4):
        K.box(f"win{k}", (0.07, 0.004, 0.06), M(BONE, wear=0.3), loc=(-0.17 + k * 0.1, -0.061, 0.06), bev=0, parent=G)
    for x in (-0.14, 0.15):
        K.cyl("wheel", 0.045, 0.004, M(GRAPHITE, wear=0.2), loc=(x, -0.061, -0.085), rot=(90, 0, 0), segs=10, bev=0, parent=G)
    for x in (-0.25, 0.25):
        K.cyl("nail", 0.008, 0.01, M(ZINC, metal=0.6), loc=(x, -0.06, 0.14), rot=(90, 0, 0), segs=6, bev=0, parent=G)
    # a tin of flowers at the end of the bench
    can = K.cyl("can", 0.06, 0.13, tin(MUSTARD, wear=0.3), loc=(-0.9, 0.1, 0.515), segs=12, bev=0.004, parent=P)
    C = Mesh()
    for k in range(5):
        a = rnd.uniform(0, 6.28)
        tip = (-0.9 + math.cos(a) * 0.08, 0.1 + math.sin(a) * 0.08, 0.78 + rnd.uniform(-0.05, 0.05))
        C.card([(-0.9, 0.1, 0.56), (-0.9 + math.cos(a) * 0.03, 0.1 + math.sin(a) * 0.03, 0.68), tip], 0.015, mat_of("stem", "#5e7f3a"))
        C.ball(tip, 0.025, mat_of("flower", (BONE, MUSTARD, BRICK)[k % 3], rough=0.7), u=5, v=3)
    C.build(P, "flowers")
    _grass(P, rnd, [(-1.2, -0.4), (1.2, -0.45), (-1.2, 0.7), (1.25, 0.65), (0.4, 0.75), (-0.6, -0.1)], h=0.35)
    return P


def roadside_shrine(seed=51):
    """A little settler roadside memorial: a box shrine of odd painted boards on a post, a corrugated gable over it, a
    lamp burning inside beside a blank picture card, a strip of bunting, tin cans of flowers and candle stubs at its
    foot in a ring of stones."""
    rnd = random.Random(seed)
    P = _root("shrine")
    K.box("post", (0.12, 0.12, 1.25), wood(3, wear=0.4), loc=(0, 0.05, 0.525), bev=0.012, parent=P)
    for s in (-1, 1):
        beam(P, f"strut{s}", (s * 0.03, 0.05, 0.7), (s * 0.22, 0.05, 1.08), 0.04, 0.04, wood(2))
    zb = 1.12
    K.box("floor", (0.56, 0.4, 0.04), wood(1, wear=0.5), loc=(0, 0.02, zb + 0.02), bev=0.006, parent=P)
    K.box("back", (0.52, 0.03, 0.5), M(SEA, wear=0.6, grime=0.4), loc=(0, 0.2, zb + 0.29), bev=0.005, parent=P)
    K.box("left", (0.03, 0.38, 0.5), M(BRICK, wear=0.6, grime=0.4), loc=(-0.265, 0.02, zb + 0.29), bev=0.005, parent=P)
    K.box("right", (0.03, 0.38, 0.5), M(BONE, wear=0.6, grime=0.4), loc=(0.265, 0.02, zb + 0.29), bev=0.005, parent=P)
    K.box("back_dark", (0.48, 0.005, 0.46), K.mat("shrine_in", "#3a3128", rough=0.9), loc=(0, 0.183, zb + 0.28), bev=0, parent=P)
    zr = zb + 0.54
    for s in (-1, 1):
        corrugated(P, f"roof{s}", 0.42, 0.52, tin(ZINC if s < 0 else MUSTARD, wear=0.25, grime=0.4), pitch=0.07, amp=0.01,
                   loc=(s * 0.15, 0.02, zr + 0.1), rot=(0, s * 35, 90))
        K.box(f"gable{s}", (0.02, 0.02, 0.36), wood(2), loc=(s * 0.13, -0.19, zr + 0.08), rot=(0, s * -55, 0), bev=0.003, parent=P)
    K.box("ridge", (0.06, 0.56, 0.04), tin(ZINC, wear=0.3), loc=(0, 0.02, zr + 0.22), bev=0.005, parent=P)
    # the lamp: a little storm lantern burning warm, and a picture card beside it
    lx, ly, lz = 0.1, 0.05, zb + 0.04
    iron = M(GRAPHITE, metal=0.5, wear=0.3)
    K.cyl("lamp_base", 0.055, 0.04, iron, loc=(lx, ly, lz + 0.02), segs=12, bev=0.005, parent=P)
    K.cyl("lamp_glass", 0.042, 0.12, K.mat("lantern_glass", "#d8d0b8", rough=0.1), loc=(lx, ly, lz + 0.1), segs=12, bev=0.004, parent=P)
    K.cyl("flame", 0.012, 0.05, glow(WARM, 4), loc=(lx, ly, lz + 0.1), segs=6, r2=0.002, bev=0, parent=P)
    K.cyl("lamp_top", 0.05, 0.035, iron, loc=(lx, ly, lz + 0.18), segs=12, r2=0.025, bev=0.004, parent=P)
    K.torus("lamp_bail", 0.04, 0.004, iron, loc=(lx, ly, lz + 0.22), rot=(90, 0, 0), arc=180, segs=10, parent=P)
    light = K.point("shrine_light", (lx, ly - 0.05, lz + 0.12), WARM, 8, 0.05)
    light.parent = P
    K.box("card_frame", (0.16, 0.015, 0.2), wood(0, wear=0.2), loc=(-0.1, 0.16, zb + 0.16), rot=(-10, 0, 6), bev=0.003, parent=P)
    K.box("card", (0.12, 0.004, 0.15), M(BONE, wear=0.2, grime=0.3, rough=0.8), loc=(-0.1, 0.151, zb + 0.163), rot=(-10, 0, 6), bev=0, parent=P)
    # bunting across the front
    cols = (SIGNAL, MUSTARD, SEA, BONE, STEEL)
    for k in range(5):
        x = -0.22 + k * 0.11
        sag = 0.03 * math.sin(math.pi * (k + 0.5) / 5)
        plate_xz(f"flag{k}", [(-0.035, 0), (0.035, 0), (0, -0.07)], 0.003, M(cols[k], wear=0.2, grime=0.3, rough=0.8),
                 loc=(x, -0.19, zr - 0.02 - sag), parent=P, bev=0)
    rod(P, "bunting_line", (-0.26, -0.19, zr - 0.01), (0.26, -0.19, zr - 0.01), 0.003, rope_mat(), segs=4)
    # the foot: stones, cans of flowers and candle stubs
    C = Mesh()
    stone = mat_of("fieldstone", "#80827a", rough=0.9, noise_scale=4.0)
    for k in range(9):
        a = 2 * math.pi * k / 9 + rnd.uniform(-0.15, 0.15)
        C.ball((math.cos(a) * 0.4, -0.05 + math.sin(a) * 0.35, 0.03), rnd.uniform(0.05, 0.08), stone, scale=(1.3, 1.0, 0.6), u=6, v=4)
    for k in range(4):
        a = rnd.uniform(-2.7, -0.4)
        C.turn([(0.012, 0.0), (0.012, rnd.uniform(0.04, 0.09))], mat_of("wax", BONE, rough=0.6), segs=6,
               mx=Matrix.Translation((math.cos(a) * 0.24, -0.05 + math.sin(a) * 0.22, 0.0)))
    for k, (x, y, col) in enumerate(((-0.2, -0.22, STEEL), (0.22, -0.18, BRICK))):
        K.cyl(f"can{k}", 0.05, 0.11, tin(col, wear=0.3), loc=(x, y, 0.055), segs=10, bev=0.004, parent=P)
        for j in range(4):
            a = rnd.uniform(0, 6.28)
            tip = (x + math.cos(a) * 0.07, y + math.sin(a) * 0.07, 0.3 + rnd.uniform(-0.04, 0.04))
            C.card([(x, y, 0.1), (x + math.cos(a) * 0.02, y + math.sin(a) * 0.02, 0.2), tip], 0.014, mat_of("stem", "#5e7f3a"))
            C.ball(tip, 0.024, mat_of("flower", (BONE, MUSTARD, BRICK, SEA)[(j + k) % 4], rough=0.7), u=5, v=3)
    C.build(P, "offerings")
    _grass(P, rnd, [(0.3, 0.25), (-0.3, 0.2), (0.0, 0.35)], h=0.3)
    return P


def hitchhiker_sign(seed=52):
    """A hitchhiker's spot: a cardboard sign propped on a plank crate, scribbled over in black marker with an arrow and
    loops (no letters), a canvas pack slumped against the crate and a water bottle."""
    rnd = random.Random(seed)
    P = _root("hitch")
    crate(P, "crate", (0.55, 0.4, 0.42), (0.0, 0.05, 0.0), yaw=4, shade=1)
    G = _empty(P, "sign", (0.0, -0.17, 0.42), (-14, 0, 3))
    card = K.mat("cardboard", CARDBOARD, rough=0.95, grime=0.35, noise_scale=8.0)
    plate_xz("card", [(-0.32, 0.0), (0.32, 0.0), (0.33, 0.4), (0.25, 0.43), (-0.31, 0.42)], 0.008, card, loc=(0, 0, 0), parent=G, bev=0.002)
    K.box("crease", (0.62, 0.003, 0.006), K.mat("card_d", "#8e6f44", rough=0.95), loc=(0, -0.005, 0.2), bev=0, parent=G)
    ink = K.mat("marker", "#1d1d20", rough=0.6)
    # an arrow pointing along the road, then loops and a wavy underline: scribble, no letters
    rod(G, "arrow_shaft", (-0.22, -0.006, 0.31), (0.16, -0.006, 0.31), 0.008, ink, segs=4)
    rod(G, "arrow_a", (0.16, -0.006, 0.31), (0.09, -0.006, 0.36), 0.008, ink, segs=4)
    rod(G, "arrow_b", (0.16, -0.006, 0.31), (0.09, -0.006, 0.26), 0.008, ink, segs=4)
    pts = [(-0.24 + 0.04 * t, -0.006, 0.17 + 0.05 * math.sin(t * 1.9) + 0.012 * math.cos(t * 3.7)) for t in range(12)]
    for k in range(len(pts) - 1):
        rod(G, f"scrib{k}", pts[k], pts[k + 1], 0.006, ink, segs=4)
    for k in range(6):
        rod(G, f"under{k}", (-0.22 + k * 0.08, -0.006, 0.07 + 0.01 * (k % 2)), (-0.14 + k * 0.08, -0.006, 0.07 + 0.01 * ((k + 1) % 2)), 0.005, ink, segs=4)
    # the pack and the bottle
    canvas = K.mat("pack", "#6f7a5a", rough=0.95, grime=0.35, noise_scale=8.0)
    lump(P, "pack", 0.24, (0.45, -0.05, 0.22), (0.9, 0.7, 1.05), canvas, rnd, jitter=0.06, rot=(0, -14, 20), u=10, v=7)
    K.box("pack_flap", (0.3, 0.2, 0.06), canvas, loc=(0.42, -0.08, 0.46), rot=(0, -20, 20), bev=0.02, parent=P)
    for s in (-1, 1):
        rod(P, f"strap{s}", (0.36, -0.16 + s * 0.06, 0.4), (0.33, -0.2 + s * 0.06, 0.1), 0.012, K.mat("strap", "#3d3a30", rough=0.8), segs=4)
    K.box("pack_roll", (0.32, 0.12, 0.12), K.mat("bedroll", BRICK, rough=0.95), loc=(0.45, -0.02, 0.52), rot=(0, -14, 20), bev=0.05, parent=P)
    K.cyl("bottle", 0.035, 0.22, K.mat("bottle", "#6f8a86", rough=0.15), loc=(-0.38, -0.2, 0.11), segs=10, bev=0.01, parent=P)
    K.cyl("bottle_cap", 0.018, 0.03, M(SIGNAL, wear=0.2), loc=(-0.38, -0.2, 0.235), segs=8, bev=0.004, parent=P)
    _grass(P, rnd, [(-0.3, 0.25), (0.3, 0.3), (-0.5, -0.1)], h=0.3)
    return P


def abandoned_shopping_cart(seed=53):
    """A shopping cart left in the verge: a zinc wire basket on its frame, mustard plastic on the handle, one castor
    gone so it lists on that corner, a sack and two tins still in it and grass growing up through the wheels."""
    rnd = random.Random(seed)
    P = _root("cart")
    G = _empty(P, "list", (0, 0, 0), (0, -3.5, 0))
    wire = _galv(wear=0.2, grime=0.5, rough=0.45)
    L, W, Hb, zb = 0.9, 0.55, 0.42, 0.42
    t = 0.008
    # basket wires: floor, sides and ends
    for k in range(11):
        x = -L / 2 + L * k / 10
        K.box("floor_w", (t, W, t), wire, loc=(x, 0, zb), bev=0, parent=G)
        for s in (-1, 1):
            K.box("side_v", (t, t, Hb), wire, loc=(x, s * W / 2, zb + Hb / 2), bev=0, parent=G)
    for k in range(6):
        y = -W / 2 + W * k / 5
        for s in (-1, 1):
            K.box("end_v", (t, t, Hb), wire, loc=(s * L / 2, y, zb + Hb / 2), bev=0, parent=G)
    for k in range(4):
        z = zb + Hb * k / 3
        for s in (-1, 1):
            K.box("side_h", (L, t * 1.4, t * 1.4), wire, loc=(0, s * W / 2, z), bev=0, parent=G)
            K.box("end_h", (t * 1.4, W, t * 1.4), wire, loc=(s * L / 2, 0, z), bev=0, parent=G)
    K.box("floor_l", (L, t * 1.4, t * 1.4), wire, loc=(0, 0, zb), bev=0, parent=G)
    # chassis: two rails, the lower tray, four castor posts (one castor gone)
    frame = _galv(wear=0.3, grime=0.55)
    for s in (-1, 1):
        rod(G, f"rail{s}", (-L / 2 - 0.05, s * 0.22, 0.12), (L / 2, s * 0.22, 0.12), 0.012, frame, segs=6)
        rod(G, f"leg_f{s}", (L / 2, s * 0.22, 0.12), (L / 2, s * W / 2, zb), 0.012, frame, segs=6)
        rod(G, f"leg_b{s}", (-L / 2 - 0.05, s * 0.22, 0.12), (-L / 2, s * W / 2, zb), 0.012, frame, segs=6)
        rod(G, f"handle_up{s}", (-L / 2, s * W / 2, zb + Hb), (-L / 2 - 0.12, s * W / 2, zb + Hb + 0.12), 0.012, frame, segs=6)
    for k in range(5):
        K.box("tray", (0.006, 0.44, 0.006), wire, loc=(-0.3 + k * 0.15, 0, 0.13), bev=0, parent=G)
    K.cyl("handle", 0.02, W + 0.04, M(MUSTARD, wear=0.4, grime=0.5, rough=0.5), loc=(-L / 2 - 0.12, 0, zb + Hb + 0.12), rot=(90, 0, 0), segs=10, bev=0.005, parent=G)
    for (x, s) in ((L / 2 - 0.05, -1), (L / 2 - 0.05, 1), (-L / 2, 1), (-L / 2, -1)):
        K.box("fork", (0.03, 0.05, 0.08), frame, loc=(x, s * 0.22, 0.08), bev=0.004, parent=G)
        if (x, s) == (-L / 2, -1):
            continue  # the lost castor
        K.cyl("castor", 0.05, 0.03, K.mat("tyre", TYRE, rough=0.9, grime=0.4), loc=(x, s * 0.22, 0.05), rot=(90, 0, 0), segs=10, bev=0.006, parent=G)
    # what was left in it
    sack(G, "sack", (0.4, 0.3, 0.22), (0.1, 0.05, zb + 0.005), (0, 0, 20), K.mat("sacking", SACK, rough=0.95, grime=0.4, noise_scale=10.0), rnd)
    for k in range(2):
        K.cyl(f"tin{k}", 0.04, 0.1, tin((SIGNAL, STEEL)[k], wear=0.3), loc=(-0.25 + k * 0.1, -0.12, zb + 0.06), rot=(0, 90 * k, 0), segs=10, bev=0.004, parent=G)
    rust_patch(G, (0.2, -W / 2 - 0.01, zb + 0.05), (0.2, 0.004, 0.06))
    _grass(P, rnd, [(0.4, -0.25), (0.45, 0.25), (-0.45, 0.25), (-0.5, -0.2), (0.0, 0.1)], h=0.35)
    return P


def roadside_tire_pile(seed=54):
    """A heap of worn tyres dumped at the roadside: a bottom row lying flat, more thrown on top, two leaning against
    the heap and one still on its bone-painted rim standing on edge, grass and weeds growing through."""
    rnd = random.Random(seed)
    P = _root("tyrepile")
    # a bottom ring of three lying flat, two thrown on them, one on top tipped up
    for k, (x, y) in enumerate(((-0.33, -0.1), (0.33, -0.12), (0.0, 0.42))):
        tyre_ring(P, f"low{k}", (x, y, 0.1), rot=(rnd.uniform(-3, 3), rnd.uniform(-3, 3), 0), r=0.33, w=0.2)
    for k, (x, y, z, rx, ry) in enumerate(((-0.15, 0.05, 0.3, 9, -6), (0.22, 0.2, 0.32, -7, 11))):
        tyre_ring(P, f"mid{k}", (x, y, z), rot=(rx, ry, 0), r=0.32, w=0.2)
    tyre_ring(P, "top", (0.02, 0.1, 0.56), rot=(24, 10, 0), r=0.31, w=0.19)
    # two leaning on the heap and one still on its rim, rolled against it
    tyre_ring(P, "lean_a", (-0.72, 0.15, 0.31), rot=(0, 64, 12), r=0.33, w=0.21)
    tyre_ring(P, "lean_b", (0.25, -0.62, 0.31), rot=(66, 0, -14), r=0.32, w=0.2)
    G = _empty(P, "rimmed", (0.78, 0.25, 0.0), (0, 0, 70))
    R = _empty(G, "tip", (0, 0, 0.0), (0, -14, 0))
    tyre(R, "rim", (0, 0, 0.36), 0.36, 0.2, rim=M(BONE, metal=0.3, wear=0.6, grime=0.5), axis="x")
    _grass(P, rnd, [(-0.45, 0.1), (0.2, 0.05), (0.6, 0.6), (-0.6, 0.6), (1.0, 0.3), (-1.0, -0.3)], h=0.4)
    return P


def road_cone_cluster(seed=55):
    """Colony road cones round a broken patch of road: four standing and one knocked over, a striped mustard and
    graphite bar slung between two, and the cracked tarmac patch they guard."""
    rnd = random.Random(seed)
    P = _root("cones")
    spots = [(-0.8, -0.5, 0), (0.8, -0.55, 12), (0.75, 0.6, -8), (-0.85, 0.5, 20)]
    for k, (x, y, yaw) in enumerate(spots):
        c = traffic_cone()
        c.parent = P
        c.location = (x, y, 0)
        c.rotation_euler = (0, 0, math.radians(yaw))
    c = traffic_cone()
    c.parent = P
    c.location = (-0.35, -1.15, 0.16)
    c.rotation_euler = (0, math.radians(76), math.radians(165))
    # a bar between the two front cones, its stripes in mustard and graphite
    G = _empty(P, "bar", (0.0, -0.52, 0.62), (0, 0, -1.5))
    for k in range(8):
        K.box(f"bar{k}", (0.2, 0.04, 0.04), M(MUSTARD if k % 2 == 0 else GRAPHITE, wear=0.4, grime=0.4), loc=(-0.7 + k * 0.2, 0, 0), bev=0.006, parent=G)
    for s in (-1, 1):
        K.box(f"bar_cap{s}", (0.06, 0.06, 0.08), M(GRAPHITE, wear=0.3), loc=(s * 0.82, 0, -0.02), bev=0.008, parent=G)
    # the patch: a darker cracked square of tarmac, a pothole in it
    tar = K.mat("tarmac", "#3c3d3f", rough=0.9, grime=0.2, cracks=0.8, noise_scale=4.0)
    K.box("patch", (1.4, 0.95, 0.02), tar, loc=(0.0, 0.0, 0.01), rot=(0, 0, 3), bev=0.005, parent=P)
    K.disc("pothole", 0.25, K.mat("pothole", SHADOW, rough=1.0), (0.15, 0.05), z=0.022, segs=10, wobble=0.25, seed=seed).parent = P
    for k in range(5):
        lump(P, f"chunk{k}", rnd.uniform(0.04, 0.07), (rnd.uniform(-0.5, 0.6), rnd.uniform(-0.3, 0.4), 0.03), (1.4, 1.0, 0.4), tar, rnd, jitter=0.2, u=6, v=4)
    return P


def broken_down_scooter(seed=56):
    """A colony motor scooter that gave up at the roadside, 1.75 m long: a steel-blue leg shield and a bone tail body
    (its rounded volume), a graphite seat split open, a flat rear tyre, its side cover off and lying in the grass so
    the engine shows, propped on its side stand."""
    rnd = random.Random(seed)
    P = _root("scooter")
    G = _empty(P, "lean", (0, 0, 0), (7, 0, 0))     # leaning onto its stand (+y side up)
    blue = M(STEEL, metal=0.3, wear=0.45, grime=0.45, scratches=0.5)
    bone = M(BONE, metal=0.2, wear=0.5, grime=0.45, scratches=0.5)
    dark = M(GRAPHITE, metal=0.4, wear=0.3, grime=0.4)
    rimm = M(ZINC, metal=0.6, wear=0.3)
    tyre(G, "front", (0.62, 0, 0.2), 0.2, 0.1, rim=rimm)
    tyre(G, "rear", (-0.58, 0, 0.17), 0.2, 0.11, flat=0.18, rim=rimm)
    # floorboard and frame
    K.box("floor", (0.55, 0.32, 0.05), dark, loc=(0.1, 0, 0.27), bev=0.012, parent=G)
    K.slab("tail", 0.8, 0.4, 0.34, 0.15, bone, loc=(-0.42, 0, 0.5), bev=0.04, parent=G)
    K.slab("tail_top", 0.62, 0.3, 0.06, 0.12, bone, loc=(-0.42, 0, 0.69), bev=0.02, parent=G)
    K.box("seat", (0.62, 0.3, 0.1), K.mat("seat", "#2f3135", rough=0.7, grime=0.3, cracks=0.3), loc=(-0.38, 0, 0.77), bev=0.03, parent=G)
    K.box("seat_split", (0.2, 0.05, 0.02), K.mat("foam", "#cdbf8f", rough=0.95), loc=(-0.3, -0.06, 0.825), rot=(0, 0, 25), bev=0.005, parent=G)
    K.box("rack", (0.22, 0.24, 0.02), dark, loc=(-0.82, 0, 0.72), bev=0.004, parent=G)
    # front: leg shield, steering column, handlebar and the round headlamp
    sh = _empty(G, "shield", (0.42, 0, 0.55), (0, -14, 0))
    K.slab("shield", 0.08, 0.42, 0.62, 0.035, blue, loc=(0, 0, 0), bev=0.02, parent=sh)
    K.slab("mudguard", 0.36, 0.14, 0.06, 0.06, blue, loc=(0.62, 0, 0.42), rot=(0, 10, 0), bev=0.015, parent=G)
    rod(G, "column", (0.6, 0, 0.28), (0.5, 0, 1.0), 0.03, dark, segs=8)
    rod(G, "fork", (0.62, 0, 0.2), (0.6, 0, 0.42), 0.02, dark, segs=6)
    K.box("bar_body", (0.14, 0.2, 0.08), blue, loc=(0.5, 0, 1.02), bev=0.02, parent=G)
    rod(G, "bars", (0.48, -0.32, 1.04), (0.48, 0.32, 1.04), 0.014, dark, segs=6)
    for s in (-1, 1):
        K.cyl(f"grip{s}", 0.02, 0.1, K.mat("grip", "#232427", rough=0.8), loc=(0.48, s * 0.3, 1.04), rot=(90, 0, 0), segs=8, bev=0.004, parent=G)
    rod(G, "mirror_stem", (0.48, -0.2, 1.05), (0.46, -0.24, 1.22), 0.006, dark, segs=4)
    K.cyl("mirror", 0.045, 0.012, dark, loc=(0.46, -0.24, 1.25), rot=(0, 90, 0), segs=10, bev=0.003, parent=G)
    K.cyl("lamp", 0.07, 0.06, bone, loc=(0.58, 0, 1.0), rot=(0, 90, 0), segs=12, bev=0.01, parent=G)
    K.cyl("lamp_glass", 0.055, 0.01, K.mat("lampglass", GLASS_L, rough=0.1), loc=(0.615, 0, 1.0), rot=(0, 90, 0), segs=12, bev=0, parent=G)
    # the engine behind the missing side cover (on -y), and the stand
    FR.framed_box(G, "engine", (-0.5, -0.12, 0.34), (0.38, 0.18, 0.22), M(GRAPHITE, metal=0.5, wear=0.2, grime=0.6), M(ZINC, metal=0.6, wear=0.2), fw=0.03)
    rod(G, "exhaust", (-0.35, 0.2, 0.25), (-0.85, 0.2, 0.3), 0.035, M(RUST, metal=0.5, wear=0.2, grime=0.5), segs=10)
    rod(G, "stand", (-0.1, 0.17, 0.25), (-0.18, 0.4, 0.0), 0.012, dark, segs=6)
    K.box("plate", (0.012, 0.18, 0.1), M(BONE, wear=0.3), loc=(-0.88, 0, 0.6), bev=0.003, parent=G)
    stencil(G, "C-17", (-0.895, 0, 0.6), 0.045, dark, rot=(90, 0, -90), extrude=0.003)
    rust_patch(G, (-0.2, -0.2, 0.42), (0.18, 0.004, 0.06))
    # the side cover, lying in the grass
    K.slab("cover", 0.5, 0.3, 0.05, 0.12, bone, loc=(-0.45, -0.75, 0.04), rot=(0, 0, 12), bev=0.015, parent=P)
    _grass(P, rnd, [(0.62, -0.1), (-0.58, 0.1), (-0.45, -0.95), (0.0, 0.35), (-0.9, -0.3)], h=0.32)
    return P


def dropped_cargo_spill(seed=57):
    """Cargo fallen off a colony truck on a bend: four framed bone-and-steel crates tumbled across the verge, one burst
    open with its tins and parts spilt, a pallet broken under them and the mustard ratchet strap that gave way."""
    rnd = random.Random(seed)
    P = _root("cargo")
    panel = M(BONE, wear=0.55, grime=0.5, scratches=0.5)
    frame_m = M(STEEL, metal=0.35, wear=0.5, grime=0.45)
    crates = [((-0.9, 0.3, 0.4), (0.8, 0.8, 0.8), (0, 0, 12)), ((0.25, 0.55, 0.35), (0.9, 0.7, 0.7), (0, 0, -25)),
              ((1.25, -0.25, 0.32), (0.7, 0.6, 0.64), (8, 0, 40)), ((0.05, 0.6, 1.05), (0.7, 0.6, 0.6), (0, 0, -8))]
    for k, (loc, size, rot) in enumerate(crates):
        G = _empty(P, f"crate{k}", loc, rot)
        FR.framed_box(G, f"c{k}", (0, 0, 0), size, panel, frame_m, fw=0.07, screws=None)
        if k < 2:
            stencil(G, ("CW-2", "CW-7")[k], (0, -size[1] / 2 - 0.012, size[2] * 0.15), 0.1, _dark(), extrude=0.003)
        if k < 2:
            FR.hazard(G, (0, -size[1] / 2 - 0.006, -size[2] * 0.25), size[0] * 0.6, 0.06, M(MUSTARD, wear=0.5), _dark(), n=4)
    # the burst crate on its side, its lid off and a side panel gone, its load out on the grass
    B = _empty(P, "burst", (-0.35, -0.75, 0.33), (0, 90, -30))
    S = 0.66
    m = frame_m
    for a in (-1, 1):
        for b in (-1, 1):
            K.box("bx", (S, 0.06, 0.06), m, loc=(0, a * (S / 2 - 0.03), b * (S / 2 - 0.03)), bev=0.012, parent=B)
            K.box("bz", (0.06, 0.06, S), m, loc=(a * (S / 2 - 0.03), b * (S / 2 - 0.03), 0), bev=0.012, parent=B)
    K.box("panel_back", (0.02, S - 0.08, S - 0.08), panel, loc=(-S / 2 + 0.03, 0, 0), bev=0.004, parent=B)
    K.box("panel_side", (S - 0.08, 0.02, S - 0.08), panel, loc=(0, S / 2 - 0.03, 0), bev=0.004, parent=B)
    K.box("panel_dark", (S - 0.1, S - 0.1, 0.01), K.mat("crate_in", "#4b4740", rough=0.9), loc=(0, 0, -S / 2 + 0.05), bev=0, parent=B)
    K.box("lid", (0.7, 0.7, 0.03), panel, loc=(-0.95, -1.05, 0.02), rot=(0, 4, 22), bev=0.008, parent=P)
    for k in range(7):
        x, y = rnd.uniform(-0.2, 0.7), rnd.uniform(-1.6, -0.9)
        K.cyl(f"tin{k}", 0.05, 0.13, tin((STEEL, SIGNAL, MUSTARD, ZINC)[k % 4], wear=0.3), loc=(x, y, 0.05), rot=(90, 0, rnd.uniform(0, 180)), segs=8, bev=0, parent=P)
    for k in range(3):
        K.box(f"partbox{k}", (0.24, 0.16, 0.12), K.mat("card", "#b08d5a", rough=0.95, grime=0.35), loc=(0.6 + k * 0.25, -1.0 + rnd.uniform(-0.2, 0.2), 0.06),
              rot=(0, 0, rnd.uniform(0, 90)), bev=0.01, parent=P)
    # the broken pallet half under the first crate
    for k in range(4):
        plank(P, f"pallet{k}", (1.0, 0.1, 0.025), (-0.95, -0.05 + k * 0.14, 0.03), rot=(0, 3 * (k % 2), 15 + rnd.uniform(-3, 3)), shade=k)
    plank(P, "pallet_split", (0.6, 0.1, 0.025), (-1.6, -0.5, 0.02), rot=(0, 0, 70), shade=2)
    # the strap
    pts = [(-1.6, 0.9, 0.01), (-0.9, 0.75, 0.81), (-0.2, 0.95, 0.01), (0.4, 1.2, 0.01), (1.1, 1.0, 0.01)]
    for k in range(len(pts) - 1):
        beam(P, f"strap{k}", pts[k], pts[k + 1], 0.05, 0.004, M(MUSTARD, wear=0.2, grime=0.45, rough=0.8), bev=0)
    K.box("ratchet", (0.14, 0.07, 0.05), _dark(), loc=(1.15, 1.0, 0.03), rot=(0, 0, -20), bev=0.008, parent=P)
    _grass(P, rnd, [(-1.4, -0.2), (1.6, 0.3), (0.0, -0.2), (-0.4, 1.2), (1.4, -0.9)], h=0.35)
    return P


def skid_marks_debris(seed=58):
    """The marks of a crash on the road, flat enough to drive and walk over: two black tyre streaks swerving across 7 m,
    headlamp glass, a hubcap, a torn piece of bone bumper, a mud flap and bits of mustard indicator plastic."""
    rnd = random.Random(seed)
    P = _root("skid")
    sk = K.mat("scuff", SCUFF, rough=0.85, noise_scale=8.0)
    for k, off in enumerate((-0.8, 0.8)):
        pts = [(-3.5 + 7.0 * t / 10, 0.9 * math.sin(t / 10 * 2.6 - 0.8) + off * (1 - 0.15 * t / 10), 0) for t in range(11)]
        s = K.strip(f"streak{k}", pts, 0.2 - 0.015 * k, sk, z=0.006 + 0.001 * k)
        s.parent = P
    for k in range(14):
        sz = rnd.uniform(0.03, 0.09)
        pts = [(0, 0), (sz, rnd.uniform(-0.02, 0.02)), (rnd.uniform(0.2, 0.8) * sz, sz * 0.8)]
        plate_xz(f"shard{k}", pts, 0.005, K.mat("lampglass", GLASS_L, rough=0.1, wear=0.2), loc=(rnd.uniform(1.6, 3.2), rnd.uniform(-0.8, 0.9), 0.01),
                 rot=(90, 0, rnd.uniform(0, 360)), parent=P, bev=0)
    K.cyl("hubcap", 0.19, 0.03, M(ZINC, metal=0.7, wear=0.3, grime=0.4), loc=(2.6, -1.1, 0.016), rot=(4, 0, 0), segs=16, r2=0.15, bev=0.008, parent=P)
    for k in range(6):
        a = 2 * math.pi * k / 6
        K.box("hub_rib", (0.12, 0.012, 0.006), M(ZINC, metal=0.7, wear=0.3), loc=(2.6 + math.cos(a) * 0.08, -1.1 + math.sin(a) * 0.08, 0.034), rot=(0, 0, math.degrees(a)), bev=0, parent=P)
    G = _empty(P, "bumper", (1.2, 1.2, 0.0), (0, 0, 18))
    for k in range(4):
        K.box(f"bump{k}", (0.32, 0.12, 0.04), M(BONE, wear=0.5, grime=0.5, scratches=0.6), loc=(k * 0.3, 0.02 * k * k, 0.03), rot=(0, 0, k * 10), bev=0.012, parent=G)
    K.box("flap", (0.3, 0.4, 0.012), K.mat("rubber", TYRE, rough=0.9, grime=0.4), loc=(-1.6, 1.3, 0.008), rot=(0, 0, 30), bev=0.004, parent=P)
    for k in range(5):
        K.box(f"indicator{k}", (rnd.uniform(0.04, 0.09), rnd.uniform(0.03, 0.06), 0.008), M(MUSTARD, rough=0.25, wear=0.2), loc=(rnd.uniform(1.5, 3.0), rnd.uniform(-0.5, 0.6), 0.006),
              rot=(0, 0, rnd.uniform(0, 180)), bev=0.002, parent=P)
    for k in range(4):
        lump(P, f"dirt{k}", rnd.uniform(0.08, 0.14), (rnd.uniform(-1.0, 1.5), rnd.uniform(-0.8, 0.8), 0.0), (1.6, 1.0, 0.25), K.mat("thrown_earth", "#5e4a38", rough=1.0, noise_scale=6.0),
             rnd, jitter=0.25, u=6, v=4, flat=0.0)
    return P


PROPS = {
    "guard_rail": guard_rail,
    "guard_rail_bent": guard_rail_bent,
    "road_sign_junction": road_sign_junction,
    "road_sign_warning_curve": road_sign_warning_curve,
    "road_sign_animal": road_sign_animal,
    "km_marker": km_marker,
    "road_reflector_post": road_reflector_post,
    "bus_stop_bench_settler": bus_stop_bench_settler,
    "roadside_shrine": roadside_shrine,
    "hitchhiker_sign": hitchhiker_sign,
    "abandoned_shopping_cart": abandoned_shopping_cart,
    "roadside_tire_pile": roadside_tire_pile,
    "road_cone_cluster": road_cone_cluster,
    "broken_down_scooter": broken_down_scooter,
    "colony_cable_pole": colony_cable_pole,
    "roadside_billboard_small": roadside_billboard_small,
    "dropped_cargo_spill": dropped_cargo_spill,
    "skid_marks_debris": skid_marks_debris,
}
