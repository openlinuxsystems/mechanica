"""The Colony Works industrial kit (docs/art-direction.md): the modules the colony's big buildings are assembled from.

Bone ribbed cladding held in steel-blue posts and floor-line belts, framed corner posts with graphite corner blocks and
hazard-banded bases, a concrete plinth, framed windows (some broken), roller doors, a parapet with its coping, gutter
and downpipes, floor slabs with edge beams, a switchback stair core, an outside fire escape with balconies, catwalks
and railings, rooftop vents, fans, ducts and skylights, a rooftop capsule tank, a tall flue stack and a tower crane
(the two silhouette pieces that are not props already), painted floor numbers, the colony logo panel and hazard bands,
plus the yard pieces no prop covers yet (a floodlight mast and a welded-mesh fence with a gate).

Every module is placed by loc (its origin) and yaw (degrees about +Z) under a parent and returns its group empty.
Walls are built in a local frame where the wall runs along +x, its outer face is the plane y = 0 and outside is -y.
Sizes are on the 0.25 m build grid. Drawn parts never collide: each module adds plain collider_box shapes for what a
player should stand on or bump into (walls, slabs, stair ramps, railings), so the collider stays simple and nothing
snags. warehouses.py assembles the warehouses from these.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import frame as FR
from families import M, glow, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, RUST, ZINC, GUNMETAL, CONCRETE, WARM, SEA
from props_colony_b import beam as _beam, rod, plate_xz

STOREY = 4.0            # floor to floor (the warehouses' floors are every 4 m)
SLAB = 0.3              # slab thickness; the ground slab's top is at 0.3
BAY = 5.0               # post spacing along a wall; panels are half a bay
PLINTH = 1.0            # concrete base course height
PARAPET = 1.0           # parapet above the roof deck
RISE = 0.25             # stair riser (the player steps 0.35 m)
CLAD_DEPTH = 0.06       # rib depth of the cladding
CREST = -CLAD_DEPTH     # y of the rib crests in a wall's frame
GLASS = "#3b4b55"
VOID = "#17191c"


# ---------------------------------------------------------------- materials
def bone(shade=0):
    """Bone cladding; three batches of paint so a long wall is not one flat colour."""
    return M(("#d9d1bc", "#d2c9b2", "#ddd6c4")[shade % 3], wear=0.35, grime=0.6, scratches=0.25)


def steel():
    return M(STEEL, metal=0.3, wear=0.4, grime=0.5)


def mustard(wear=0.45):
    return M(MUSTARD, wear=wear, grime=0.4)


def graphite():
    return M(GRAPHITE, metal=0.3, wear=0.35)


def gunmetal():
    return M(GUNMETAL, metal=0.5, wear=0.3)


def zinc():
    return M(ZINC, metal=0.6, wear=0.25, grime=0.35)


def concrete():
    return K.mat("worn_concrete", CONCRETE, rough=0.9, grime=0.45, noise_scale=2, ao=0.4)


def roof_felt():
    return M("#5a5f63", rough=0.9, wear=0.05, grime=0.0, scratches=0.0)


def rust():
    return M(RUST, metal=0.2, wear=0.1, grime=0.3, rough=0.9, scratches=0)


def rust_light():
    return M("#7f5d48", metal=0.1, wear=0.0, grime=0.2, rough=0.9, scratches=0)


def glass():
    return K.mat("window_glass", GLASS, rough=0.06, metal=0.35, ao=0.2)


def void():
    return K.mat("void", VOID, rough=1.0, ao=0)


# ---------------------------------------------------------------- helpers
def beam(P, name, a, b, w, mat, d=None, bev=0.0):
    """A square bar from a to b (unbevelled: the kit has hundreds)."""
    return _beam(P, name, a, b, w, mat, d=d, bev=bev)


def quad(G, name, pts, mat, y=0.0):
    """A single face in the x-z plane at y, facing -y (glass, paint, stains): no thickness, no back."""
    bm = bmesh.new()
    vs = [bm.verts.new((x, y, z)) for x, z in pts]
    f = bm.faces.new(vs)
    f.normal_update()
    if f.normal.y > 0:
        f.normal_flip()
    ob = K.from_bm(name, bm, mat)
    ob.parent = G
    return quiet(ob)


def framed(G, name, center, size, panel, frame, fw=0.1, screws=None):
    """A light framed box for the kit's small cases (panel inside corner rails, a rim top and bottom): the part
    cases' look for a tenth of frame.framed_box's triangles."""
    cx, cy, cz = center
    sx, sy, sz = size
    quiet(K.box(f"{name}_core", (sx - 0.04, sy - 0.04, sz - 0.04), panel, loc=center, bev=0.01, parent=G))
    for a in (-1, 1):
        for b in (-1, 1):
            quiet(K.box(f"{name}_rail", (fw, fw, sz), frame, loc=(cx + a * (sx - fw) / 2, cy + b * (sy - fw) / 2, cz), bev=0, parent=G))
    for c in (-1, 1):
        quiet(K.box(f"{name}_rim", (sx + 0.02, sy + 0.02, fw * 0.8), frame, loc=(cx, cy, cz + c * (sz - fw * 0.8) / 2), bev=0, parent=G))
    if screws is not None:
        for a in (-1, 1):
            quiet(K.cyl(f"{name}_screw", fw * 0.22, 0.03, screws, loc=(cx + a * (sx - fw) / 2, cy - sy / 2 - 0.01, cz), rot=(90, 0, 0), segs=6, bev=0, parent=G))


def lighten(root, keep=3.0):
    """For the game export: drops the bevels of parts under keep metres (bolts, rails, frames, treads), which the
    eye cannot see on a building, keeping them on posts, belts, copings and slabs."""
    bpy.context.view_layer.update()
    for ob in root.children_recursive:
        if ob.type == "MESH" and max(ob.dimensions) < keep:
            for m in [m for m in ob.modifiers if m.type == "BEVEL"]:
                ob.modifiers.remove(m)


def rect_minus(x0, x1, z0, z1, holes):
    """The rectangle [x0, x1] x [z0, z1] less any number of rectangular holes, as few rectangles as columns allow."""
    xs = sorted({x0, x1} | {v for h in holes for v in (h[0], h[1]) if x0 < v < x1})
    cols = []
    for a, b in zip(xs[:-1], xs[1:]):
        m = (a + b) / 2
        spans = [(z0, z1)]
        for hx0, hx1, hz0, hz1 in holes:
            if hx0 < m < hx1:
                nxt = []
                for c, d in spans:
                    if hz1 <= c or hz0 >= d:
                        nxt.append((c, d))
                        continue
                    if hz0 > c:
                        nxt.append((c, hz0))
                    if hz1 < d:
                        nxt.append((hz1, d))
                spans = nxt
        if cols and cols[-1][2] == spans:
            cols[-1][1] = b
        else:
            cols.append([a, b, spans])
    return [(a, b, c, d) for a, b, spans in cols for c, d in spans if b - a > 1e-3 and d - c > 1e-3]


def group(P, name, loc=(0, 0, 0), yaw=0.0):
    g = K.link(bpy.data.objects.new(name, None))
    g.location = loc
    g.rotation_euler = (0, 0, math.radians(yaw))
    g.parent = P
    return g


def quiet(*obs):
    """Drawn only: the module's colliders stand in for it."""
    for ob in obs:
        if ob is not None and ob.get("collide") != "only" and not ob.get("collider_only"):
            ob["collide"] = "never"
    return obs[0] if len(obs) == 1 else obs


def quiet_all(G):
    for ob in [G] + list(G.children_recursive):
        if ob.type in ("MESH", "FONT"):
            quiet(ob)
    return G


def solid(G, name, size, loc, rot=(0, 0, 0)):
    """A collision-only box (walls, slabs, rails) in G's frame."""
    return K.collider_box(G, name, size, loc, rot)


def ramp(G, name, a, b, width, base=0.4):
    """A collision-only slab whose top runs from a to b (a flight walked as a ramp)."""
    a, b = Vector(a), Vector(b)
    run = Vector((b.x - a.x, b.y - a.y, 0))
    u = Vector((-run.y, run.x, 0)).normalized() * width / 2
    bm = bmesh.new()
    top = [bm.verts.new(p) for p in (a - u, b - u, b + u, a + u)]
    bot = [bm.verts.new(v.co - Vector((0, 0, base))) for v in top]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((top[j], top[i], bot[i], bot[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(f"col_{name}", bm, None)
    ob.parent = G
    return K.collider_only(ob)


def _rib(u, pitch):
    """The cladding's trapezoid profile, 0 in the valley and 1 on the crest, for u in [0, pitch)."""
    s = pitch / 0.5
    a, b, c, d = 0.2 * s, 0.26 * s, 0.44 * s, 0.5 * s
    if u < a:
        return 0.0
    if u < b:
        return (u - a) / (b - a)
    if u < c:
        return 1.0
    return max(0.0, 1.0 - (u - c) / (d - c))


def corrugated(G, name, x0, x1, z0, z1, mat, y=0.0, pitch=0.625, depth=CLAD_DEPTH, t=0.03, horizontal=False):
    """A sheet of ribbed cladding from x0 to x1 and z0 to z1 in G's frame, valleys on the plane y and crests depth
    proud of it towards -y, ribs vertical (or horizontal, for roller door curtains). Ribs fall on multiples of pitch in
    G's frame, so the pieces round a window line up."""
    if x1 - x0 < 1e-3 or z1 - z0 < 1e-3:
        return None
    a0, a1 = (z0, z1) if horizontal else (x0, x1)
    s = pitch / 0.5
    cuts = {a0, a1}
    k = math.floor(a0 / pitch)
    while k * pitch < a1:
        for f in (0.0, 0.2, 0.26, 0.44):
            v = k * pitch + f * s
            if a0 < v < a1:
                cuts.add(v)
        k += 1
    cuts = sorted(cuts)
    bm = bmesh.new()

    def prof(v):
        return y - depth * _rib((v - math.floor(v / pitch + 1e-9) * pitch), pitch)

    if horizontal:
        lo = [bm.verts.new((x0, prof(v), v)) for v in cuts]
        hi = [bm.verts.new((x1, prof(v), v)) for v in cuts]
        for i in range(len(cuts) - 1):
            bm.faces.new((lo[i], lo[i + 1], hi[i + 1], hi[i])[::-1])
    else:
        lo = [bm.verts.new((v, prof(v), z0)) for v in cuts]
        hi = [bm.verts.new((v, prof(v), z1)) for v in cuts]
        for i in range(len(cuts) - 1):
            bm.faces.new((lo[i], lo[i + 1], hi[i + 1], hi[i]))
    # the flat back and the two ends
    bk = [bm.verts.new(p) for p in ((x0, y + t, z0), (x1, y + t, z0), (x1, y + t, z1), (x0, y + t, z1))]
    bm.faces.new((bk[3], bk[2], bk[1], bk[0]))
    if horizontal:
        bm.faces.new((lo[0], hi[0], bk[1], bk[0]))
        bm.faces.new((hi[-1], lo[-1], bk[3], bk[2]))
    else:
        bm.faces.new((lo[0], bk[0], bk[3], hi[0]))
        bm.faces.new((lo[-1], hi[-1], bk[2], bk[1]))
    ob = K.from_bm(name, bm, mat)
    ob.parent = G
    return quiet(ob)


def grime_streak(G, x, z_top, length, rnd):
    """A dirty water stain running down the cladding from an edge: three narrowing strips that follow the ribs."""
    w = rnd.uniform(0.35, 0.8)
    for k, (fw, fl, col) in enumerate(((1.0, 0.45, "#8e8670"), (0.6, 0.8, "#a19883"), (0.3, 1.0, "#ada58f"))):
        L = length * fl
        corrugated(G, "grime_streak", x + w * (1 - fw) / 2, x + w * (1 + fw) / 2, z_top - L, z_top,
                   M(col, wear=0.0, grime=0.2, scratches=0, rough=0.8), y=-0.003 - 0.002 * k, t=0.002)


def holes_cut(x0, x1, z0, z1, holes):
    """The rectangles left of [x0, x1] x [z0, z1] round holes (hx0, hx1, hz0, hz1) that do not overlap in x."""
    out, edge = [], x0
    for hx0, hx1, hz0, hz1 in sorted(holes):
        hx0, hx1 = max(hx0, x0), min(hx1, x1)
        if hx1 <= hx0 or hz1 <= z0 or hz0 >= z1:
            continue
        out.append((edge, hx0, z0, z1))
        out.append((hx0, hx1, z0, max(z0, hz0)))
        out.append((hx0, hx1, min(z1, hz1), z1))
        edge = hx1
    out.append((edge, x1, z0, z1))
    return [r for r in out if r[1] - r[0] > 1e-3 and r[3] - r[2] > 1e-3]


def clip_poly(pts, x0, x1, z0, z1):
    """Clips a convex polygon in the x-z plane to a rectangle (Sutherland-Hodgman)."""
    def clip(poly, inside, cross):
        out = []
        for i, p in enumerate(poly):
            q = poly[(i + 1) % len(poly)]
            if inside(p):
                out.append(p)
                if not inside(q):
                    out.append(cross(p, q))
            elif inside(q):
                out.append(cross(p, q))
        return out

    def at_x(xv):
        return lambda p, q: (xv, p[1] + (q[1] - p[1]) * (xv - p[0]) / (q[0] - p[0]))

    def at_z(zv):
        return lambda p, q: (p[0] + (q[0] - p[0]) * (zv - p[1]) / (q[1] - p[1]), zv)
    for inside, cross in ((lambda p: p[0] >= x0, at_x(x0)), (lambda p: p[0] <= x1, at_x(x1)),
                          (lambda p: p[1] >= z0, at_z(z0)), (lambda p: p[1] <= z1, at_z(z1))):
        pts = clip(pts, inside, cross)
        if len(pts) < 3:
            return []
    return pts


def streak(G, x, z_top, length, y, w=0.14, seed=0):
    """A rust streak running down from a bolt or a sill: three overlapping strips, narrowing and fading as they fall,
    standing just proud of the plane y."""
    rnd = random.Random(seed)
    w0 = w * rnd.uniform(0.8, 1.3)
    for k, (fw, fl, mat) in enumerate(((1.0, 0.35, rust()), (0.6, 1.0, rust_light()))):
        L = length * fl * rnd.uniform(0.85, 1.1)
        xx = x + rnd.uniform(-0.02, 0.02)
        hw = w0 * fw / 2
        quad(G, "rust_streak", [(xx - hw, z_top), (xx + hw, z_top), (xx + hw * 0.7, z_top - L), (xx - hw * 0.7, z_top - L)], mat, y=y - 0.003 - 0.002 * k)


# ================================================================== hazard bands, labels, numbers, logo
def hazard_band(P, loc, w, h, yaw=0.0, stripe=None, name="hazard"):
    """A mustard band with graphite stripes at 45 degrees, on a plate facing -y (corners, door jambs, slab edges)."""
    G = group(P, name, loc, yaw)
    stripe = stripe or max(0.15, h * 0.7)
    quiet(K.box("haz_back", (w, 0.02, h), mustard(0.5), loc=(0, 0, h / 2), bev=0, parent=G))
    x = -w / 2 - h
    while x < w / 2:
        pts = clip_poly([(x, 0), (x + stripe, 0), (x + stripe + h, h), (x + h, h)], -w / 2, w / 2, 0, h)
        if len(pts) >= 3:
            quad(G, "haz_stripe", pts, graphite(), y=-0.014)
        x += 2 * stripe
    return G


def painted(G, text, loc, size, mat, rot=(90, 0, 0)):
    """Stencilled paint, a hair proud of the surface it is on."""
    ob = FR.label(G, text, loc, size, mat, rot=rot, extrude=0.0)
    ob.data.resolution_u = 3
    return quiet(ob)


def floor_number(P, n, loc, yaw=0.0, size=1.5, ribbed=True):
    """A big painted floor number: a mustard square painted over the ribs with the numeral in graphite."""
    G = group(P, f"floor_number_{n}", loc, yaw)
    if ribbed:
        corrugated(G, "number_paint", -size / 2, size / 2, 0, size, mustard(0.35), y=-0.006, t=0.004)
        y = CREST - 0.012
    else:
        quiet(K.box("number_paint", (size, 0.02, size), mustard(0.35), loc=(0, -0.01, size / 2), bev=0.005, parent=G))
        y = -0.022
    painted(G, str(n), (0, y, size * 0.48), size * 0.8, graphite())
    return G


def emblem(G, loc, r, ring_mat, mark_mat, y=0.0):
    """The colony's mark: a ring round a rising chevron over a bar (the settlement under its sky), on a plate facing -y."""
    x, _, z = loc
    quiet(K.cyl("emblem_disc", r, 0.05, steel(), loc=(x, y, z), rot=(90, 0, 0), segs=32, bev=0.01, parent=G))
    quiet(K.torus("emblem_ring", r * 0.82, r * 0.08, ring_mat, loc=(x, y - 0.03, z), rot=(90, 0, 0), segs=36, parent=G))
    for s in (-1, 1):
        quiet(K.box("emblem_chevron", (r * 0.78, 0.05, r * 0.2), mark_mat, loc=(x + s * r * 0.24, y - 0.04, z + r * 0.12),
                    rot=(0, s * 38, 0), bev=0.01, parent=G))
    quiet(K.box("emblem_bar", (r * 1.0, 0.05, r * 0.14), ring_mat, loc=(x, y - 0.04, z - r * 0.36), bev=0.01, parent=G))


def logo_panel(P, loc, yaw=0.0, w=8.0, h=2.5, code="STORES 07"):
    """The colony logo panel: a framed steel-blue board with the mark and COLONY WORKS in bone, a short code under it,
    and two warm lamps on arms lighting it."""
    G = group(P, "logo_panel", loc, yaw)
    framed(G, "logo_board", (0, -0.12, h / 2), (w, 0.24, h), steel(), graphite(), fw=0.14, screws=zinc())
    emblem(G, (-w / 2 + h * 0.55, 0, h / 2), h * 0.36, M(BONE, wear=0.3, grime=0.2), mustard(0.3), y=-0.25)
    tx = -w / 2 + h * 1.1
    big = min(h * 0.3, (w / 2 - tx - 0.35) / 7.6)     # COLONY WORKS is about 7.6 times its size wide
    lab = painted(G, "COLONY WORKS", (tx, -0.25, h * 0.6), big, M(BONE, wear=0.3, grime=0.2))
    lab.data.align_x = "LEFT"
    lab = painted(G, code, (tx, -0.25, h * 0.27), min(h * 0.17, big * 0.6), mustard(0.2))
    lab.data.align_x = "LEFT"
    for s in (-1, 1):
        x = s * w * 0.3
        quiet(K.box("logo_lamp_arm", (0.08, 0.7, 0.08), graphite(), loc=(x, -0.5, h + 0.3), bev=0.01, parent=G))
        quiet(K.box("logo_lamp", (0.6, 0.28, 0.16), graphite(), loc=(x, -0.85, h + 0.3), rot=(-25, 0, 0), bev=0.03, parent=G))
        quiet(K.box("logo_lamp_lens", (0.5, 0.2, 0.02), glow(WARM, 8), loc=(x, -0.85, h + 0.21), rot=(-25, 0, 0), bev=0, parent=G))
    for k in range(3):
        streak(G, -w / 2 + 0.6 + k * w * 0.37, 0.02, 0.9, -0.0, seed=k + 3)
    return G


# ================================================================== walls
def window(G, x, z, w=2.5, h=1.5, broken=(), y=0.0, name="window"):
    """A framed window, 3 x 2 lights, its frame proud of the rib crests; the lights listed in broken (0-5) have lost
    their glass, leaving a few shards in the frame."""
    gra = graphite()
    fw = 0.12
    yf = CREST - 0.05             # frame face, proud of the crests
    for sx in (-1, 1):
        quiet(K.box("win_jamb", (fw, 0.26, h + fw), gra, loc=(x + sx * (w / 2 - fw / 2), y - 0.05, z + h / 2), bev=0, parent=G))
    quiet(K.box("win_head", (w + 0.1, 0.28, fw), gra, loc=(x, y - 0.06, z + h - fw / 2), bev=0, parent=G))
    quiet(K.box("win_sill", (w + 0.3, 0.36, 0.1), steel(), loc=(x, y - 0.1, z - 0.02), bev=0, parent=G))
    lw = (w - 2 * fw) / 3
    for k in (1, 2):
        mx = x - w / 2 + fw + k * lw
        quad(G, "win_mullion", [(mx - 0.035, z), (mx + 0.035, z), (mx + 0.035, z + h - fw), (mx - 0.035, z + h - fw)], gra, y=y - 0.02)
    zt = z + h * 0.62
    quad(G, "win_transom", [(x - w / 2 + fw, zt - 0.035), (x + w / 2 - fw, zt - 0.035), (x + w / 2 - fw, zt + 0.035), (x - w / 2 + fw, zt + 0.035)], gra, y=y - 0.021)
    if broken:
        quiet(K.box("win_reveal", (w - 2 * fw, 0.02, h - fw), void(), loc=(x, y + 0.2, z + h / 2), bev=0, parent=G))
    rnd = random.Random(int(x * 13 + z * 7))
    if not broken:
        quad(G, "win_glass", [(x - w / 2 + fw, z), (x + w / 2 - fw, z), (x + w / 2 - fw, z + h - fw), (x - w / 2 + fw, z + h - fw)], glass(), y=y + 0.02)
        return G
    for k in range(6):
        col, row = k % 3, k // 3
        lx = x - w / 2 + fw + (col + 0.5) * lw
        z0, z1 = (z + 0.03, zt) if row == 0 else (zt, z + h - fw)
        if k in broken:
            # shards left in the frame's corners
            for c in range(rnd.randint(1, 3)):
                cx = lx - lw / 2 if c % 2 == 0 else lx + lw / 2
                cz = z0 if c < 2 else z1
                dx = (lw * rnd.uniform(0.3, 0.6)) * (1 if c % 2 == 0 else -1)
                dz = (z1 - z0) * rnd.uniform(0.35, 0.8) * (1 if cz == z0 else -1)
                quad(G, "shard", [(cx, cz), (cx + dx, cz), (cx + dx * 0.15, cz + dz)], glass(), y=y + 0.02)
            continue
        quad(G, "win_glass", [(lx - lw / 2, z0), (lx + lw / 2, z0), (lx + lw / 2, z1), (lx - lw / 2, z1)], glass(), y=y + 0.02)
    return G


def louvre(G, x, z, w=2.0, h=1.25, y=0.0):
    """A big louvred vent in a graphite frame, dark behind the blades."""
    quiet(K.box("louvre_back", (w, 0.02, h), void(), loc=(x, y + 0.1, z + h / 2), bev=0, parent=G))
    for sx in (-1, 1):
        quiet(K.box("louvre_jamb", (0.1, 0.24, h), graphite(), loc=(x + sx * (w / 2 - 0.05), y - 0.04, z + h / 2), bev=0.01, parent=G))
    for sz in (0, 1):
        quiet(K.box("louvre_rail", (w, 0.24, 0.1), graphite(), loc=(x, y - 0.04, z + 0.05 + sz * (h - 0.1)), bev=0.01, parent=G))
    n = int(h / 0.22)
    for k in range(n):
        quiet(K.box("louvre_blade", (w - 0.2, 0.2, 0.03), gunmetal(), loc=(x, y - 0.02, z + 0.12 + k * (h - 0.2) / max(1, n - 1)),
                    rot=(-35, 0, 0), bev=0, parent=G))
    return G


def wall_fan(G, x, z, r=0.6, y=0.0):
    """A round extract fan in a square graphite housing."""
    quiet(K.box("fan_housing", (2 * r + 0.3, 0.3, 2 * r + 0.3), graphite(), loc=(x, y - 0.12, z), bev=0.04, parent=G))
    quiet(K.cyl("fan_well", r, 0.05, void(), loc=(x, y - 0.27, z), rot=(90, 0, 0), segs=24, bev=0, parent=G))
    quiet(K.torus("fan_guard", r, 0.035, zinc(), loc=(x, y - 0.3, z), rot=(90, 0, 0), segs=14, parent=G))
    for k in range(3):
        quiet(K.box("fan_wire", (2 * r, 0.02, 0.025), zinc(), loc=(x, y - 0.3, z), rot=(0, 60 * k, 0), bev=0, parent=G))
    for k in range(5):
        quiet(K.box("fan_blade", (r * 0.85, 0.02, 0.22), gunmetal(), loc=(x + 0.42 * r * math.cos(k * 1.2566), y - 0.26, z + 0.42 * r * math.sin(k * 1.2566)),
                    rot=(0, -math.degrees(k * 1.2566) + 20, 0), bev=0, parent=G))


def personnel_door(G, x, z, y=0.0, w=1.25, h=2.5, number=None):
    """A steel walk-in door, shut, in a graphite frame with a warm lamp over it."""
    quiet(K.box("pdoor", (w, 0.08, h), steel(), loc=(x, y - 0.02, z + h / 2), bev=0.02, parent=G))
    for sx in (-1, 1):
        quiet(K.box("pdoor_jamb", (0.14, 0.24, h + 0.14), graphite(), loc=(x + sx * (w / 2 + 0.07), y - 0.06, z + h / 2 + 0.07), bev=0.015, parent=G))
    quiet(K.box("pdoor_head", (w + 0.42, 0.26, 0.14), graphite(), loc=(x, y - 0.07, z + h + 0.14), bev=0.015, parent=G))
    quiet(K.box("pdoor_kick", (w - 0.1, 0.02, 0.3), zinc(), loc=(x, y - 0.07, z + 0.2), bev=0, parent=G))
    quiet(K.box("pdoor_vision", (0.3, 0.02, 0.5), glass(), loc=(x - 0.2, y - 0.07, z + 1.6), bev=0, parent=G))
    quiet(K.box("pdoor_bar", (0.7, 0.07, 0.06), zinc(), loc=(x + 0.05, y - 0.1, z + 1.05), bev=0.01, parent=G))
    quiet(K.box("pdoor_lamp", (0.5, 0.22, 0.14), graphite(), loc=(x, y - 0.2, z + h + 0.45), bev=0.03, parent=G))
    quiet(K.box("pdoor_lens", (0.42, 0.16, 0.02), glow(WARM, 10), loc=(x, y - 0.22, z + h + 0.375), bev=0, parent=G))
    if number is not None:
        painted(G, str(number), (x + w / 2 + 0.75, CREST - 0.01, z + 1.9), 0.9, mustard(0.3))


def roller_door(P, loc, w=6.0, h=3.75, open_h=3.0, yaw=0.0, name="roller_door", lamp=True):
    """A roller door in a wall's opening (bottom centre at loc, wall face y = 0): hazard-striped jambs with guide
    channels, a steel-blue drum housing over the head, a ribbed curtain stopped open_h above the sill (0: shut) with a
    mustard bottom bar, a control box, bollards and a light bar."""
    G = group(P, name, loc, yaw)
    gra, stl = graphite(), steel()
    for s in (-1, 1):
        jx = s * (w / 2 + 0.2)
        quiet(K.box("door_jamb", (0.4, 0.5, h + 0.4), gra, loc=(jx, -0.2, (h + 0.4) / 2), bev=0.04, parent=G))
        hazard_band(G, (jx, -0.46, 0.0), 0.4, 1.5, name="jamb_haz")
        quiet(K.box("door_guide", (0.14, 0.2, h), gunmetal(), loc=(s * (w / 2 - 0.05), 0.06, h / 2), bev=0.01, parent=G))
        quiet(K.cyl("bollard", 0.17, 1.1, mustard(), loc=(s * (w / 2 + 0.85), -0.9, 0.55), segs=16, bev=0.03, parent=G))
        quiet(K.cyl("bollard_cap", 0.18, 0.1, gra, loc=(s * (w / 2 + 0.85), -0.9, 1.12), segs=16, bev=0.02, parent=G))
    framed(G, "door_housing", (0, -0.5, h + 0.55), (w + 0.6, 0.9, 0.9), stl, gra, fw=0.1)
    hazard_band(G, (0, -0.96, h + 0.12), w + 0.4, 0.22, name="head_haz")
    if open_h < h - 0.01:
        corrugated(G, "door_curtain", -w / 2, w / 2, open_h, h, M(STEEL, metal=0.35, wear=0.3, grime=0.45), y=0.12, pitch=0.25, depth=0.04, horizontal=True)
        quiet(K.box("door_bar", (w - 0.04, 0.14, 0.16), mustard(0.5), loc=(0, 0.06, open_h + 0.08), bev=0.02, parent=G))
        quiet(K.box("door_seal", (w - 0.04, 0.1, 0.05), M("#26282a", rough=0.9), loc=(0, 0.06, open_h - 0.02), bev=0, parent=G))
    if open_h < h - 0.01:
        solid(G, "door_curtain", (w, 0.3, h - open_h), (0, 0.12, (h + open_h) / 2))
    framed(G, "door_ctrl", (w / 2 + 0.9, -0.12, 1.4), (0.4, 0.2, 0.55), M(BONE, wear=0.3), gra, fw=0.04)
    quiet(*[c for c in G.children if c.name.startswith("door_ctrl")])
    quiet(K.cyl("door_btn", 0.05, 0.04, M(SIGNAL), loc=(w / 2 + 0.9, -0.24, 1.45), rot=(90, 0, 0), segs=10, bev=0, parent=G))
    if lamp:
        quiet(K.box("door_lightbar", (w - 1.0, 0.2, 0.12), glow(WARM, 10), loc=(0, -1.0, h + 1.08), bev=0.02, parent=G))
        K.point("door_light", (0, -1.5, h + 0.8), WARM, 220, 0.3).parent = G
    return G


def wall(P, name, length, floors, loc=(0, 0, 0), yaw=0.0, bays=None, plan=None, holes=(), seed=1, parapet=True, posts=True):
    """A building wall from x = -length/2 to length/2: the plinth, ribbed bone cladding in storey bands up to the
    parapet, steel-blue posts at the bay lines, floor-line belts, the coping, and what plan(bay, floor) puts in each
    bay: 'window', 'broken', 'louvre', 'fan', 'porthole', 'door' (a walk-in door, shut) or 'plain'. holes are openings (x0, x1, z0, z1)
    cut through it all (roller doors), which the caller fills. Collides as boxes round the holes."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    H = STOREY * floors + SLAB
    top = H + (PARAPET if parapet else 0.15)
    bays = bays or [BAY] * int(round(length / BAY))
    edges = [-length / 2]
    for b in bays:
        edges.append(edges[-1] + b)
    plan = plan or (lambda b, f: "plain")
    holes = list(holes)
    shade = [rnd.randrange(3) for _ in bays]
    # plinth
    for x0, x1, z0, z1 in holes_cut(-length / 2, length / 2, 0, PLINTH, holes):
        quiet(K.box("plinth", (x1 - x0, 0.42, z1 - z0), concrete(), loc=((x0 + x1) / 2, 0.03, (z0 + z1) / 2), bev=0.04, parent=G))
        quiet(K.box("plinth_cap", (x1 - x0, 0.48, 0.08), M(GRAPHITE, wear=0.4, moss=0.06), loc=((x0 + x1) / 2, 0.02, z1 - 0.02), bev=0.02, parent=G)) if z1 >= PLINTH - 1e-6 else None
    # storey bands (the last runs up into the parapet)
    bands = []
    for f in range(floors):
        z0 = PLINTH if f == 0 else STOREY * f + SLAB + 0.15
        z1 = STOREY * (f + 1) + SLAB - 0.15
        bands.append((f, z0, z1))
    bands.append((floors, H + 0.15, top))
    for bi, (x0, x1) in enumerate(zip(edges[:-1], edges[1:])):
        cx = (x0 + x1) / 2
        bay_cuts = []
        for f, z0, z1 in bands:
            kind = plan(bi, f) if f < floors else "plain"
            cut = [h for h in holes]
            base = STOREY * f + SLAB
            if kind in ("window", "broken") and x1 - x0 >= 3.5:
                ww = 2.5 if x1 - x0 < 7 else 5.0
                cut.append((cx - ww / 2, cx + ww / 2, base + 1.2, base + 2.7))
                n_broken = rnd.choice((1, 2, 2, 3, 4)) if kind == "broken" else 0
                if ww > 2.5:
                    for s in (-1, 1):
                        br = tuple(rnd.sample(range(6), n_broken)) if (kind == "broken" and (s > 0 or rnd.random() < 0.5)) else ()
                        window(G, cx + s * 1.25, base + 1.2, 2.5, 1.5, broken=br)
                else:
                    window(G, cx, base + 1.2, 2.5, 1.5, broken=tuple(rnd.sample(range(6), n_broken)))
                for s in (-1, 1):
                    if rnd.random() < 0.55:
                        streak(G, cx + s * (ww / 2 - 0.2) + rnd.uniform(-0.1, 0.1), base + 1.15, rnd.uniform(0.6, 1.1), CREST - 0.002, seed=rnd.randrange(999))
            elif kind == "louvre":
                cut.append((cx - 1.0, cx + 1.0, base + 1.5, base + 2.75))
                louvre(G, cx, base + 1.5, 2.0, 1.25)
            elif kind == "fan":
                wall_fan(G, cx, base + 2.4, 0.6)
            elif kind == "porthole":
                cut.append((cx - 0.75, cx + 0.75, base + 1.3, base + 2.8))
                porthole(G, cx, base + 2.05)
            elif kind == "door":
                cut.append((cx - 0.8, cx + 0.8, base, base + 2.85))
                personnel_door(G, cx, base, number=f + 1 if f > 0 else None)
            bay_cuts += [c for c in cut if c not in holes]
            if kind == "plain" and f < floors and rnd.random() < 0.25:
                streak(G, rnd.uniform(x0 + 0.5, x1 - 0.5), z1, rnd.uniform(0.8, 1.8), CREST - 0.002, w=0.1, seed=rnd.randrange(999))
        # the bay's cladding, storey to storey behind the belts, round its openings
        for r in rect_minus(x0, x1, PLINTH, top, holes + bay_cuts):
            corrugated(G, "cladding", r[0], r[1], r[2], r[3], bone(shade[bi]))
        # weather: dirty water streaks down from the coping and the belts, following the ribs
        for f, z0, z1 in bands:
            for _ in range(rnd.choice((0, 1, 1, 2)) if f == floors else rnd.choice((0, 0, 1))):
                sx = rnd.uniform(x0 + 0.4, x1 - 0.9)
                if any(h[0] - 1.0 < sx < h[1] + 0.2 for h in holes + bay_cuts if h[3] > z1 - 3.0 and h[2] < z1):
                    continue
                grime_streak(G, sx, z1, rnd.uniform(1.2, 3.4) if f == floors else rnd.uniform(0.8, 2.0), rnd)
        # a panel seam batten at mid bay
        if posts and x1 - x0 >= 4.0:
            for r in _vcut(cx - 0.04, cx + 0.04, PLINTH, top, holes + bay_cuts):
                quad(G, "seam", [(cx - 0.04, r[2]), (cx + 0.04, r[2]), (cx + 0.04, r[3]), (cx - 0.04, r[3])], graphite(), y=CREST - 0.008)
    # posts at the bay lines (the ends are the corner posts')
    if posts:
        for x in edges[1:-1]:
            if any(h[0] - 0.3 < x < h[1] + 0.3 and h[2] < 0.5 for h in holes):
                continue
            post(G, x, top)
    # floor-line belts and the coping
    for f in range(1, floors + 1):
        z = STOREY * f + SLAB
        for x0, x1, _, _ in holes_cut(-length / 2, length / 2, z - 0.15, z + 0.15, holes):
            quiet(K.box("belt", (x1 - x0, 0.22, 0.3), steel(), loc=((x0 + x1) / 2, CREST - 0.06, z), bev=0.03, parent=G))
    if parapet:
        coping(G, length, top, holes)
    # collider: the wall's thickness round the holes, up to the coping
    for x0, x1, z0, z1 in holes_cut(-length / 2, length / 2, 0, top, holes):
        solid(G, "wall", (x1 - x0, 0.4, z1 - z0), ((x0 + x1) / 2, 0.0, (z0 + z1) / 2))
    return G


def _vcut(x0, x1, z0, z1, holes):
    """The pieces of a thin vertical strip [x0, x1] x [z0, z1] left between holes that cross it."""
    spans = [(z0, z1)]
    for hx0, hx1, hz0, hz1 in holes:
        if hx1 <= x0 or hx0 >= x1:
            continue
        nxt = []
        for a, b in spans:
            if hz1 <= a or hz0 >= b:
                nxt.append((a, b))
                continue
            if hz0 > a:
                nxt.append((a, hz0))
            if hz1 < b:
                nxt.append((hz1, b))
        spans = nxt
    return [(x0, x1, a, b) for a, b in spans if b - a > 0.05]


def post(G, x, top, w=0.45):
    """A steel-blue wall post with a base plate and bolts."""
    quiet(K.box("post", (w, 0.28, top), steel(), loc=(x, CREST - 0.1, top / 2), bev=0.04, parent=G))
    quiet(K.box("post_shoe", (w + 0.2, 0.4, 0.3), graphite(), loc=(x, CREST - 0.1, PLINTH + 0.15), bev=0.02, parent=G))


def coping(G, length, top, holes=()):
    """The parapet's coping: a graphite cap with a drip edge over a steel-blue rail, stopping short of the corner
    columns (which stand taller) and at any gap in the parapet."""
    for x0, x1, _, _ in holes_cut(-length / 2 + 0.3, length / 2 - 0.3, top - 0.3, top + 0.2, holes):
        quiet(K.box("coping", (x1 - x0, 0.5, 0.14), M(GRAPHITE, metal=0.3, wear=0.35, moss=0.05), loc=((x0 + x1) / 2, 0.1, top + 0.07), bev=0.03, parent=G))
        quiet(K.box("coping_rail", (x1 - x0, 0.24, 0.24), steel(), loc=((x0 + x1) / 2, CREST - 0.07, top - 0.12), bev=0.03, parent=G))


def corner_post(P, loc, top, name="corner"):
    """A framed corner column (0.75 m square) with graphite corner blocks at the floor lines, bolted, a concrete
    footing and hazard bands round its foot where trucks clip it. loc is the building's corner; it stands on it."""
    G = group(P, name, loc)
    stl, gra = steel(), graphite()
    quiet(K.box("corner_footing", (1.2, 1.2, 0.35), concrete(), loc=(0, 0, 0.175), bev=0.04, parent=G))
    quiet(K.box("corner_column", (0.75, 0.75, top + 0.3), stl, loc=(0, 0, (top + 0.3) / 2), bev=0.06, parent=G))
    for z in [PLINTH + 0.2] + [STOREY * f + SLAB for f in range(1, int((top - 1) / STOREY) + 1)] + [top + 0.12]:
        quiet(K.box("corner_block", (0.92, 0.92, 0.42), gra, loc=(0, 0, z), bev=0.04, parent=G))
        for a in range(4):
            ang = a * math.pi / 2
            for t in (0.0,):
                bx, by = 0.465 * math.cos(ang) - t * math.sin(ang), 0.465 * math.sin(ang) + t * math.cos(ang)
                quiet(K.box("corner_bolt", (0.09, 0.09, 0.09), zinc(), loc=(bx, by, z), rot=(0, 0, math.degrees(ang)), bev=0, parent=G))
    for yaw, (dx, dy) in ((0, (0, -0.39)), (90, (0.39, 0)), (180, (0, 0.39)), (270, (-0.39, 0))):
        hazard_band(G, (dx, dy, 0.4), 0.75, 1.0, yaw=yaw, name="corner_haz")
    streak(G, 0.2, top - 0.35, 1.4, -0.39, seed=7)
    solid(G, "corner", (0.8, 0.8, top), (0, 0, top / 2))
    return G


def downpipe(P, loc, top, name="downpipe"):
    """A round downpipe from a hopper head under the coping to a shoe at the plinth, on brackets. loc is the wall
    point it is fixed to (outside towards -y)."""
    G = group(P, name, loc)
    m = steel()
    y = -0.45
    quiet(K.box("hopper", (0.45, 0.4, 0.4), m, loc=(0, y, top - 0.45), bev=0.03, parent=G))
    quiet(K.cyl("downpipe", 0.11, top - 1.2, m, loc=(0, y, (top - 0.4) / 2 + 0.25), segs=12, bev=0, parent=G))
    quiet(K.cyl("pipe_shoe", 0.11, 0.4, m, loc=(0, y - 0.15, 0.3), rot=(-50, 0, 0), segs=12, bev=0, parent=G))
    for z in range(2, int(top - 1), 3):
        quiet(K.box("pipe_clip", (0.3, 0.32, 0.06), graphite(), loc=(0, y + 0.16, z), bev=0.01, parent=G))
    return G


def canopy(P, loc, w, depth=3.0, yaw=0.0, name="canopy", ties=True):
    """A cantilevered steel canopy over a door (loc: on the wall face at its underside's height, centre): steel-blue
    tapered cantilevers, a ribbed graphite deck, a steel-blue fascia with a mustard edge, light bars under it, and tie
    rods back to the wall (ties=False under a sign, where the cantilevers carry it alone)."""
    G = group(P, name, loc, yaw)
    n = max(2, round(w / 3.0) + 1)
    for k in range(n):
        x = -w / 2 + 0.2 + k * (w - 0.4) / (n - 1)
        quiet(plate_xz("canopy_arm", [(-depth, 0.0), (0.0, 0.0), (0.0, 0.55), (-depth, 0.3)], 0.22, steel(), loc=(x, 0, 0),
                       rot=(0, 0, 90), parent=G, bev=0.02))
        quiet(K.box("canopy_plate", (0.4, 0.12, 0.7), graphite(), loc=(x, -0.06, 0.3), bev=0.01, parent=G))
        if ties:
            quiet(rod(G, "canopy_tie", (x, -depth + 0.3, 0.45), (x, -0.1, 2.4), 0.03, graphite(), segs=6))
            quiet(K.box("canopy_tie_plate", (0.25, 0.1, 0.3), graphite(), loc=(x, -0.05, 2.4), bev=0.01, parent=G))
    deck = corrugated(G, "canopy_deck", -w / 2, w / 2, -depth, 0.0, M(GRAPHITE, metal=0.3, wear=0.3), y=0.0, pitch=0.5, depth=0.05)
    deck.rotation_euler = (math.radians(-90), 0, 0)
    deck.location = (0, 0, 0.32)
    quiet(K.box("canopy_fascia", (w + 0.1, 0.18, 0.5), steel(), loc=(0, -depth - 0.05, 0.3), bev=0.03, parent=G))
    quiet(K.box("canopy_edge", (w + 0.12, 0.2, 0.08), mustard(0.4), loc=(0, -depth - 0.05, 0.56), bev=0.01, parent=G))
    for x in (-w / 4, w / 4):
        quiet(K.box("canopy_light", (w / 2 - 1.0, 0.25, 0.08), glow(WARM, 10), loc=(x, -depth * 0.55, -0.02), bev=0.01, parent=G))
    K.point("canopy_lamp", (0, -depth * 0.6, -0.5), WARM, 260, 0.4).parent = G
    solid(G, "canopy", (w, depth, 0.5), (0, -depth / 2, 0.3))
    return G


def head_house(P, x0, x1, y0, y1, z, h, code, seed=1, name="head_house"):
    """The stair's head house on the roof, over the front corner: a clad box in steel-blue corner posts with a
    coping, a door onto the roof (towards +y), a louvre (towards +x) and the store's number painted big on its front.
    Its front and side are flush with the building's walls, standing on their parapets."""
    G = group(P, name)
    rnd = random.Random(seed)
    zt = z + h
    faces = (((x0 + x1) / 2, y0, 0, x1 - x0), (x1, (y0 + y1) / 2, 90, y1 - y0), ((x0 + x1) / 2, y1, 180, x1 - x0), (x0, (y0 + y1) / 2, -90, y1 - y0))
    for k, (cx, cy, yaw, L) in enumerate(faces):
        F = group(G, "hh_face", (cx, cy, 0), yaw)
        z_lo = z + PARAPET + 0.14 if k in (0, 3) else z
        cut = []
        if k == 2:
            cut = [(-0.8, 0.8, z, z + 2.85)]
            personnel_door(F, 0.0, z)
        if k == 1:
            cut = [(-1.0, 1.0, z + 1.6, z + 2.85)]
            louvre(F, 0.0, z + 1.6, 2.0, 1.25)
        for r in holes_cut(-L / 2, L / 2, z_lo, zt + 0.02, cut):
            corrugated(F, "hh_cladding", r[0], r[1], r[2], r[3], bone(k))
        quiet(K.box("hh_belt", (L, 0.22, 0.3), steel(), loc=(0, CREST - 0.06, zt - 0.4), bev=0.03, parent=F))
        if k in (1, 2):
            quiet(K.box("hh_base", (L, 0.3, 0.3), concrete(), loc=(0, 0.0, z + 0.15), bev=0.03, parent=F))
        for _ in range(2):
            grime_streak(F, rnd.uniform(-L / 2 + 0.5, L / 2 - 1.2), zt - 0.55, rnd.uniform(1.0, 2.5), rnd)
        if k == 0:
            floor_number(F, code, (0.0, 0.0, zt - 3.0), size=2.4)
    for x, y in ((x1, y1), (x1, y0), (x0, y1)):
        zb = z if (x, y) == (x1, y1) else z + PARAPET
        quiet(K.box("hh_post", (0.5, 0.5, zt - zb + 0.3), steel(), loc=(x, y, (zt + zb + 0.3) / 2), bev=0.05, parent=G))
        quiet(K.box("hh_post_cap", (0.62, 0.62, 0.3), graphite(), loc=(x, y, zt + 0.15), bev=0.03, parent=G))
    quiet(K.box("hh_roof", (x1 - x0, y1 - y0, 0.3), roof_felt(), loc=((x0 + x1) / 2, (y0 + y1) / 2, zt - 0.15), bev=0.02, parent=G))
    quiet(K.box("hh_coping", (x1 - x0 + 0.2, y1 - y0 + 0.2, 0.2), M(GRAPHITE, metal=0.3, wear=0.35), loc=((x0 + x1) / 2, (y0 + y1) / 2, zt + 0.1), bev=0.03, parent=G))
    quiet(K.box("hh_coping_in", (x1 - x0 - 0.6, y1 - y0 - 0.6, 0.22), M("#5a5f63", rough=0.9, wear=0, grime=0, scratches=0), loc=((x0 + x1) / 2, (y0 + y1) / 2, zt + 0.11), bev=0, parent=G))
    mushroom_vent(G, ((x0 + x1) / 2 + 1.0, y1 - 2.0, zt + 0.2), r=0.3, h=0.6)
    K.box("hh_antenna", (0.08, 0.08, 4.0), graphite(), loc=(x0 + 1.0, y1 - 1.0, zt + 2.2), bev=0, parent=G)
    K.cyl("hh_beacon", 0.14, 0.22, glow("#ff4a3a", 6), loc=(x0 + 1.0, y1 - 1.0, zt + 4.3), segs=10, bev=0, parent=G)
    solid(G, "head_house", (x1 - x0, y1 - y0, h), ((x0 + x1) / 2, (y0 + y1) / 2, z + h / 2))
    return G


# ================================================================== floors and stairs
def floor_slab(P, x0, x1, y0, y1, z_top, open_edges=(), name="slab"):
    """A concrete floor slab, its top at z_top, with steel-blue edge beams and a hazard band along the open edges
    (edges named 'x0', 'x1', 'y0', 'y1') and a railing on them."""
    G = group(P, name)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    quiet(K.box("slab", (x1 - x0, y1 - y0, SLAB), M("#a19e95", rough=0.9, wear=0.0, grime=0.0, scratches=0), loc=(cx, cy, z_top - SLAB / 2), bev=0.02, parent=G))
    solid(G, "slab", (x1 - x0, y1 - y0, SLAB), (cx, cy, z_top - SLAB / 2))
    for e in open_edges:
        if e in ("y0", "y1"):
            y = y0 if e == "y0" else y1
            s = -1 if e == "y0" else 1
            quiet(K.box("slab_edge", (x1 - x0, 0.2, 0.5), steel(), loc=(cx, y + s * 0.1 - s * 0.2, z_top - 0.25), bev=0.02, parent=G))
            hazard_band(G, (cx, y + s * 0.001, z_top - 0.22), x1 - x0, 0.2, yaw=0 if s < 0 else 180, name="slab_haz")
            railing(G, (x0 + 0.1, y - s * 0.12, z_top), (x1 - 0.1, y - s * 0.12, z_top))
        else:
            x = x0 if e == "x0" else x1
            s = -1 if e == "x0" else 1
            quiet(K.box("slab_edge", (0.2, y1 - y0, 0.5), steel(), loc=(x - s * 0.1, cy, z_top - 0.25), bev=0.02, parent=G))
            hazard_band(G, (x + s * 0.001, cy, z_top - 0.22), y1 - y0, 0.2, yaw=-90 if s < 0 else 90, name="slab_haz")
            railing(G, (x - s * 0.12, y0 + 0.1, z_top), (x - s * 0.12, y1 - 0.1, z_top))
    return G


def railing(G, a, b, h=1.1, posts=1.5, kick=True, top_mat=None, collide=True, first=True, lite=False):
    """A safety railing from a to b (its foot): steel posts, a mustard top rail, a mid rail and a toe board; collides
    as one thin wall so nobody walks off an edge. first=False leaves out the post at a (a run of railings sharing
    posts); lite draws plain posts and coarser rails (the many short runs round a silo roof)."""
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    n = max(1, round(L / posts))
    top_mat = top_mat or mustard(0.4)
    for k in range(0 if first else 1, n + 1):
        p = a + (b - a) * (k / n)
        quiet(K.box("rail_post", (0.06, 0.06, h), steel(), loc=(p.x, p.y, p.z + h / 2), bev=0 if lite else 0.008, parent=G))
    quiet(rod(G, "rail_top", a + Vector((0, 0, h)), b + Vector((0, 0, h)), 0.035, top_mat, segs=6 if lite else 8))
    quiet(rod(G, "rail_mid", a + Vector((0, 0, h * 0.5)), b + Vector((0, 0, h * 0.5)), 0.02, steel(), segs=4 if lite else 6))
    if kick and abs(b.z - a.z) < 0.01:
        d = (b - a).normalized()
        yaw = math.degrees(math.atan2(d.y, d.x))
        quiet(K.box("toe_board", (L, 0.02, 0.12), mustard(0.5), loc=tuple((a + b) / 2 + Vector((0, 0, 0.06))), rot=(0, 0, yaw), bev=0, parent=G))
    if collide:
        d = b - a
        yaw = math.degrees(math.atan2(d.y, d.x))
        flat = Vector((d.x, d.y, 0)).length
        pitch = math.degrees(math.atan2(d.z, flat))
        c = (a + b) / 2 + Vector((0, 0, h / 2))
        solid(G, "railing", (L, 0.08, h), tuple(c), rot=(0, -pitch, yaw))
    return G


def steel_flight(G, a, b, width, name="flight", rails=(True, True)):
    """A steel stair from its foot a to its top b (both on the walking surface): chequer-plate treads on two graphite
    stringers, mustard handrails on the sides in rails (left, right looking up it). Walked as a ramp."""
    a, b = Vector(a), Vector(b)
    run = Vector((b.x - a.x, b.y - a.y, 0))
    L, H = run.length, b.z - a.z
    n = max(1, round(H / RISE))
    d = run.normalized()
    side = Vector((-d.y, d.x, 0))
    yaw = math.degrees(math.atan2(d.y, d.x))
    tread = L / n
    for i in range(n):
        p = a + d * (tread * (i + 0.5)) + Vector((0, 0, H * (i + 1) / n))
        quiet(K.box("tread", (tread + 0.04, width - 0.12, 0.05), gunmetal(), loc=tuple(p - Vector((0, 0, 0.025))), rot=(0, 0, yaw), bev=0.005, parent=G))
        n0 = p - d * (tread / 2 + 0.021)
        nose = quad(G, "tread_nose", [(-(width - 0.12) / 2, -0.05), ((width - 0.12) / 2, -0.05), ((width - 0.12) / 2, 0.0), (-(width - 0.12) / 2, 0.0)], mustard(0.6))
        nose.location = tuple(n0)
        nose.rotation_euler = (0, 0, math.radians(yaw - 90))
    slope = math.degrees(math.atan2(H, L))
    for s in (-1, 1):
        o = side * s * (width / 2 - 0.04)
        quiet(beam(G, "stringer", a + o - Vector((0, 0, 0.15)) - d * 0.05, b + o - Vector((0, 0, 0.15)) + d * 0.02, 0.07, graphite(), d=0.3))
        if rails[0 if s > 0 else 1]:
            railing(G, a + o, b + o, h=1.0, posts=1.2, kick=False, collide=True)
    ramp(G, name, a + d * 0.0 + Vector((0, 0, H / n / 2)), b, width - 0.1, base=0.3)
    ramp(G, f"{name}_lip", b, b + d * 0.4, width - 0.1, base=0.3)
    return slope


def landing(G, x0, x1, y0, y1, z_top, mat=None, t=0.2):
    """A steel landing or balcony deck (grating on a graphite frame), its top at z_top."""
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    quiet(K.box("landing_frame", (x1 - x0, y1 - y0, t), graphite(), loc=(cx, cy, z_top - t / 2 - 0.03), bev=0.02, parent=G))
    quiet(K.box("landing_deck", (x1 - x0 - 0.08, y1 - y0 - 0.08, 0.03), mat or gunmetal(), loc=(cx, cy, z_top - 0.015), bev=0, parent=G))
    solid(G, "landing", (x1 - x0, y1 - y0, t), (cx, cy, z_top - t / 2))


def stair_core(P, loc, floors, name="stair_core"):
    """The switchback stair inside the front stairwell, one turn per storey. loc is the inner corner of the side and
    front walls at the ground slab's top; it runs +x (into the hall, 4.2 m) and +y (back, to the stairwell's edge 7.65 m
    on, where the upper slabs start). Per storey: a flight up the outer lane towards the front wall, a half landing, a
    flight back up the inner lane, and the landing on the next floor joining its slab. Treads rise 0.25 m."""
    G = group(P, name, loc)
    lane, gap = 2.0, 0.2
    xo, xi = lane / 2, lane + gap + lane / 2      # lane centres
    xw = 2 * lane + gap                         # 4.2
    y_half, y_foot, y_edge = 1.9, 4.7, 7.65
    for f in range(floors - 1):
        z = STOREY * f
        steel_flight(G, (xo, y_foot, z), (xo, y_half, z + 2.0), lane, name=f"core_a{f}", rails=(True, False))
        landing(G, 0.0, xw, 0.0, y_half, z + 2.0)
        steel_flight(G, (xi, y_half, z + 2.0), (xi, y_foot, z + 4.0), lane, name=f"core_b{f}", rails=(False, True))
        railing(G, (xw - 0.06, 0.1, z + 2.0), (xw - 0.06, y_half, z + 2.0))
        landing(G, 0.0, xw, y_foot, y_edge, z + 4.0, t=SLAB)
        railing(G, (xw - 0.06, y_foot, z + 4.0), (xw - 0.06, y_edge - 0.1, z + 4.0))
        # the support column under the half landing and the floor number at each landing
        quiet(K.box("core_column", (0.25, 0.25, 2.0), steel(), loc=(xw - 0.2, 0.2, z + 1.0), bev=0.02, parent=G))
        number_on_wall(G, f + 2, (-0.3, y_foot + 1.45, z + 4.0 + 1.2))
    if floors > 1:
        z = STOREY * (floors - 1)
        railing(G, (0.1, y_foot + 0.06, z), (lane, y_foot + 0.06, z))
    number_on_wall(G, 1, (-0.3, y_foot + 1.45, 1.2))
    return G


def number_on_wall(G, n, loc, size=1.25):
    """A floor number painted on an inside wall (facing +x)."""
    x, y, z = loc
    quiet(K.box("in_number_paint", (0.01, size, size), mustard(0.35), loc=(x, y, z + size / 2), bev=0, parent=G))
    painted(G, str(n), (x + 0.012, y, z + size * 0.48), size * 0.8, graphite(), rot=(90, 0, 90))


def fire_escape(P, loc, floors, yaw=0.0, name="fire_escape", length=7.5, doors=True):
    """An outside steel stair up a side wall to the roof: a balcony at every floor along the wall, flights all rising
    the same way in an outer lane (so each has a full storey of headroom), landings at both ends, posts to the ground
    and knee braces to the wall. loc is on the wall's outer face (its local y = 0, outside -y) at ground level, at the
    balcony's start; it runs +x for length. A walk-in door opens from each balcony near its far end."""
    G = group(P, name, loc, yaw)
    inner, outer = 1.25, 1.25
    y_in0, y_in1 = -0.35 - inner, -0.35        # balcony lane (against the posts)
    y_out0, y_out1 = y_in0 - outer, y_in0     # flight lane
    end = 1.25
    x_foot, x_top = end, length - end
    yo = (y_out0 + y_out1) / 2
    # the ground pad at the stair's foot
    quiet(K.box("escape_pad", (end + 0.5, outer + inner + 0.4, 0.3), concrete(), loc=(end / 2, (y_out0 + y_in1) / 2, 0.15), bev=0.03, parent=G))
    solid(G, "escape_pad", (end + 0.5, outer + inner + 0.4, 0.3), (end / 2, (y_out0 + y_in1) / 2, 0.15))
    levels = [SLAB] + [STOREY * f + SLAB for f in range(1, floors + 1)]
    for k in range(floors):
        za, zb = levels[k], levels[k + 1]
        steel_flight(G, (x_foot, yo, za), (x_top, yo, zb), outer, name=f"escape_{k}")
        roof = k + 1 == floors
        # landings at both ends of the next level, and the balcony between them (not on the roof)
        landing(G, x_top, length, y_out0, y_in1, zb)
        if roof:
            # a chequer plate over the wall head, through the parapet gap onto the roof
            quiet(K.box("escape_threshold", (length - x_top, 0.8, 0.03), gunmetal(), loc=((x_top + length) / 2, y_in1 + 0.4, zb - 0.015), bev=0, parent=G))
            solid(G, "escape_threshold", (length - x_top, 0.8, 0.1), ((x_top + length) / 2, y_in1 + 0.4, zb - 0.05))
        if not roof:
            landing(G, 0.0, x_top, y_in0, y_in1, zb)
            landing(G, 0.0, x_foot, y_out0, y_out1, zb)
            railing(G, (0.06, y_out0 + 0.06, zb), (x_foot, y_out0 + 0.06, zb))
            railing(G, (x_foot, y_in0 + 0.05, zb), (x_top, y_in0 + 0.05, zb))   # between the balcony and the flights
            railing(G, (0.06, y_out0 + 0.06, zb), (0.06, y_in1 - 0.06, zb))
            if doors:
                personnel_door(G, length - 0.95, zb, number=k + 2)
        railing(G, (x_top, y_out0 + 0.06, zb), (length - 0.06, y_out0 + 0.06, zb))
        railing(G, (length - 0.06, y_out0 + 0.06, zb), (length - 0.06, y_in1 - 0.06, zb))
    # posts to the ground at the outer corners, knee braces to the wall under each deck
    for x in (0.1, x_foot, x_top, length - 0.1):
        top = levels[-1] if x > x_top - 0.1 else levels[-2]
        quiet(K.box("escape_post", (0.18, 0.18, top), steel(), loc=(x, y_out0 + 0.1, top / 2), bev=0.02, parent=G))
        quiet(K.box("escape_post_shoe", (0.4, 0.4, 0.12), graphite(), loc=(x, y_out0 + 0.1, 0.06), bev=0.01, parent=G))
        solid(G, "escape_post", (0.18, 0.18, top), (x, y_out0 + 0.1, top / 2))
    for zb in levels[1:]:
        for x in (0.6, length / 2, length - 0.6):
            quiet(beam(G, "knee_brace", (x, -0.3, zb - 1.3), (x, y_in0 + 0.2, zb - 0.25), 0.1, steel()))
            quiet(K.box("wall_bracket", (0.3, 0.12, 0.3), graphite(), loc=(x, -0.36, zb - 1.3), bev=0.01, parent=G))
    return G


def ladder_caged(G, loc, h, yaw=0.0):
    """A steel ladder with a safety cage (rooftop tanks and the roof hatch), rungs every 0.3 m, facing -y."""
    L = group(G, "ladder", loc, yaw)
    for s in (-1, 1):
        quiet(K.box("ladder_stile", (0.06, 0.08, h), steel(), loc=(s * 0.25, 0, h / 2), bev=0.008, parent=L))
    for k in range(int(h / 0.3)):
        quiet(K.box("rung", (0.5, 0.035, 0.035), zinc(), loc=(0, 0, 0.3 + k * 0.3), bev=0, parent=L))
    if h > 3:
        # a squared safety cage: hoops of flat bar every 0.9 m from 2.4 m up, and five bars along it
        z = 2.4
        while z < h:
            for sx in (-1, 1):
                quiet(K.box("cage_hoop", (0.04, 0.76, 0.05), steel(), loc=(sx * 0.38, -0.38, z), bev=0, parent=L))
            quiet(K.box("cage_hoop", (0.8, 0.04, 0.05), steel(), loc=(0, -0.76, z), bev=0, parent=L))
            z += 0.9
        for bx, by in ((-0.38, -0.4), (0.38, -0.4), (-0.25, -0.76), (0.0, -0.76), (0.25, -0.76)):
            quiet(K.box("cage_bar", (0.03, 0.03, h - 2.4), steel(), loc=(bx, by, 2.4 + (h - 2.4) / 2), bev=0, parent=L))
    return L


# ================================================================== catwalks and roof
def catwalk(P, a, b, width=1.0, z=0.0, name="catwalk", rails=(True, True), brackets=True):
    """A grating catwalk from a to b (x, y) at height z: a graphite frame, gunmetal grating with its slots, railings
    on the sides in rails, and brackets back to the wall (+side) every 2.5 m."""
    G = group(P, name)
    a, b = Vector((a[0], a[1], z)), Vector((b[0], b[1], z))
    d = (b - a).normalized()
    side = Vector((-d.y, d.x, 0))
    L = (b - a).length
    yaw = math.degrees(math.atan2(d.y, d.x))
    c = (a + b) / 2
    quiet(K.box("cw_frame", (L, width, 0.18), graphite(), loc=tuple(c - Vector((0, 0, 0.11))), rot=(0, 0, yaw), bev=0.015, parent=G))
    quiet(K.box("cw_grate", (L - 0.06, width - 0.1, 0.03), gunmetal(), loc=tuple(c - Vector((0, 0, 0.015))), rot=(0, 0, yaw), bev=0, parent=G))
    for k in range(int(L / 0.6)):
        p = a + d * (0.3 + k * 0.6)
        quiet(K.box("cw_slot", (0.05, width - 0.2, 0.01), void(), loc=tuple(p + Vector((0, 0, 0.001))), rot=(0, 0, yaw), bev=0, parent=G))
    solid(G, "catwalk", (L, width, 0.2), tuple(c - Vector((0, 0, 0.1))), rot=(0, 0, yaw))
    for s, on in zip((1, -1), rails):
        if on:
            railing(G, a + side * s * (width / 2 - 0.04), b + side * s * (width / 2 - 0.04))
    if brackets:
        for k in range(int(L / 2.5) + 1):
            p = a + d * min(L, k * 2.5) + side * (width / 2)
            quiet(beam(G, "cw_bracket", tuple(p - Vector((0, 0, 0.2))), tuple(p + side * 0.25 - Vector((0, 0, 1.1))), 0.08, steel()))
    return G


def roof_deck(P, x0, x1, y0, y1, z_top, name="roof"):
    """The flat roof: a concrete deck under graphite felt laid in strips, with lap seams."""
    G = group(P, name)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    quiet(K.box("roof_slab", (x1 - x0, y1 - y0, SLAB), M("#8f8c84", rough=0.9, wear=0, grime=0, scratches=0), loc=(cx, cy, z_top - SLAB / 2 - 0.02), bev=0, parent=G))
    quiet(K.box("roof_felt", (x1 - x0, y1 - y0, 0.02), roof_felt(), loc=(cx, cy, z_top - 0.01), bev=0, parent=G))
    x = x0 + 2.0
    while x < x1 - 0.5:
        quiet(K.box("felt_seam", (0.06, y1 - y0, 0.012), M("#3c4046", rough=0.9, wear=0, grime=0, scratches=0), loc=(x, cy, z_top + 0.004), bev=0, parent=G))
        x += 2.0
    rnd = random.Random(int(x1 * 7 + y1 * 3))
    patch = M("#4b5054", rough=0.9, wear=0, grime=0, scratches=0)
    stain = M("#666a6a", rough=0.95, wear=0, grime=0, scratches=0)
    for k in range(int((x1 - x0) * (y1 - y0) / 120)):
        px, py = rnd.uniform(x0 + 2, x1 - 2), rnd.uniform(y0 + 2, y1 - 2)
        quiet(K.box("felt_patch", (rnd.uniform(1.0, 3.0), rnd.uniform(1.0, 2.5), 0.01), patch, loc=(px, py, z_top + 0.006), rot=(0, 0, rnd.choice((0, 90))), bev=0, parent=G))
    for k in range(int((x1 - x0) * (y1 - y0) / 200)):
        d = K.disc("puddle_stain", 1.0, stain, (rnd.uniform(x0 + 3, x1 - 3), rnd.uniform(y0 + 3, y1 - 3)), z=z_top + 0.003, segs=20, wobble=0.3, seed=k + 9)
        d.scale = (rnd.uniform(1.0, 3.0), rnd.uniform(0.8, 2.0), 1)
        d.parent = G
        quiet(d)
    solid(G, "roof", (x1 - x0, y1 - y0, SLAB), (cx, cy, z_top - SLAB / 2))
    return G


def mushroom_vent(P, loc, r=0.35, h=0.9):
    """A round roof vent: a stub on a flashing plate, a gap, and a cone cap."""
    G = group(P, "mushroom_vent", loc)
    quiet(K.box("vent_flash", (2 * r + 0.3, 2 * r + 0.3, 0.06), graphite(), loc=(0, 0, 0.03), bev=0.01, parent=G))
    quiet(K.cyl("vent_stub", r, h, zinc(), loc=(0, 0, h / 2), segs=16, bev=0.01, parent=G))
    quiet(K.cyl("vent_gap", r * 0.9, 0.25, void(), loc=(0, 0, h + 0.12), segs=16, bev=0, parent=G))
    quiet(K.cyl("vent_cap", r * 1.5, 0.35, zinc(), loc=(0, 0, h + 0.42), segs=16, r2=0.05, bev=0.01, parent=G))
    return G


def ac_unit(P, loc, yaw=0.0, w=3.0, d=2.0, h=1.25):
    """A rooftop air handler: a framed bone case on graphite skids, two fan rings on top, louvred sides."""
    G = group(P, "ac_unit", loc, yaw)
    for s in (-1, 1):
        quiet(K.box("ac_skid", (w + 0.3, 0.2, 0.2), graphite(), loc=(0, s * (d / 2 - 0.25), 0.1), bev=0.02, parent=G))
    framed(G, "ac_case", (0, 0, 0.2 + h / 2), (w, d, h), M(BONE, wear=0.35, grime=0.4), steel(), fw=0.12)
    for k, x in enumerate((-w / 4, w / 4)):
        quiet(K.cyl("ac_well", 0.55, 0.05, void(), loc=(x, 0, 0.2 + h + 0.01), segs=20, bev=0, parent=G))
        quiet(K.torus("ac_ring", 0.58, 0.06, graphite(), loc=(x, 0, 0.2 + h + 0.05), segs=14, parent=G))
        for bl in range(4):
            quiet(K.box("ac_blade", (1.0, 0.16, 0.02), gunmetal(), loc=(x, 0, 0.2 + h + 0.03), rot=(0, 8, 45 * bl + 20 * k), bev=0, parent=G))
    FR.vent(G, (0, -d / 2 - 0.02, 0.2 + h / 2), w * 0.6, h * 0.55, graphite(), gunmetal())
    quiet_all(G)
    solid(G, "ac_unit", (w, d, h + 0.2), (0, 0, (h + 0.2) / 2))
    return G


def duct(P, pts, w=0.8, h=0.6, name="duct"):
    """A galvanised rectangular duct along the points (x, y, z: its centre line), flanged every 1.5 m and at the
    bends, on graphite stands down to z = pts' lowest stand height when it runs level on a roof."""
    G = group(P, name)
    m = zinc()
    for i in range(len(pts) - 1):
        a, b = Vector(pts[i]), Vector(pts[i + 1])
        ob = beam(G, "duct", a, b, w, m, d=h)
        quiet(ob)
        L = (b - a).length
        for k in range(1, int(L / 1.5)):
            quiet(beam(G, "duct_flange", a + (b - a) * (k * 1.5 / L) - (b - a).normalized() * 0.03, a + (b - a) * (k * 1.5 / L) + (b - a).normalized() * 0.03,
                       w + 0.08, graphite(), d=h + 0.08))
        quiet(K.box("duct_bend", (w + 0.08, w + 0.08, h + 0.08) if abs((b - a).z) < 0.01 else (w + 0.08, h + 0.08, w + 0.08), graphite(), loc=tuple(b), bev=0.01, parent=G)) if i < len(pts) - 2 else None
    return G


def skylight(P, loc, l=6.0, w=2.0, yaw=0.0, broken=(), name="skylight"):
    """A low ridge skylight: a framed curb with two pitched glass faces in graphite glazing bars; panes in broken have
    gone, showing the dark hall."""
    G = group(P, name, loc, yaw)
    framed(G, "sky_curb", (0, 0, 0.25), (l, w, 0.5), M(BONE, wear=0.3, grime=0.3), steel(), fw=0.1)
    n = int(l / 1.0)
    for s in (-1, 1):
        for k in range(n):
            if (k, s) in broken:
                quiet(K.box("sky_hole", (l / n - 0.05, w / 2 * 1.02, 0.02), void(), loc=(-l / 2 + (k + 0.5) * l / n, s * w / 4, 0.82), rot=(s * -22, 0, 0), bev=0, parent=G))
                continue
            quiet(K.box("sky_glass", (l / n - 0.05, w / 2 * 1.02, 0.02), glass(), loc=(-l / 2 + (k + 0.5) * l / n, s * w / 4, 0.82), rot=(s * -22, 0, 0), bev=0, parent=G))
        for k in range(n + 1):
            quiet(K.box("sky_bar", (0.06, w / 2 * 1.04, 0.06), graphite(), loc=(-l / 2 + k * l / n, s * w / 4, 0.85), rot=(s * -22, 0, 0), bev=0, parent=G))
    quiet(K.box("sky_ridge", (l + 0.1, 0.14, 0.1), graphite(), loc=(0, 0, 1.03), bev=0.01, parent=G))
    quiet_all(G)
    solid(G, "skylight", (l, w, 0.6), (0, 0, 0.3))
    return G


def roof_hatch(P, loc, yaw=0.0):
    """A roof hatch on a curb, its lid shut, with grab rails."""
    G = group(P, "roof_hatch", loc, yaw)
    framed(G, "hatch_curb", (0, 0, 0.3), (1.25, 1.25, 0.6), steel(), graphite(), fw=0.08)
    quiet(K.box("hatch_lid", (1.35, 1.35, 0.08), M(BONE, wear=0.4), loc=(0, 0, 0.64), bev=0.02, parent=G))
    for s in (-1, 1):
        quiet(rod(G, "grab", (s * 0.4, -0.6, 0.6), (s * 0.4, -0.6, 1.3), 0.025, mustard(0.4)))
    quiet_all(G)
    return G


def pipe_run(P, pts, r=0.12, mat=None, name="pipe", brackets=2.0):
    """A round pipe along points, with elbows at the bends and graphite clamps every few metres."""
    G = group(P, name)
    mat = mat or M(SEA, metal=0.2, wear=0.35, grime=0.4)
    for i in range(len(pts) - 1):
        a, b = Vector(pts[i]), Vector(pts[i + 1])
        quiet(rod(G, "pipe", a, b, r, mat, segs=12))
        L = (b - a).length
        for k in range(1, int(L / brackets) + 1):
            p = a + (b - a) * min(1.0, k * brackets / L)
            quiet(rod(G, "pipe_clamp", p - (b - a).normalized() * 0.04, p + (b - a).normalized() * 0.04, r + 0.03, graphite(), segs=12))
        if 0 < i:
            quiet(K.sphere("pipe_elbow", r * 1.08, mat, loc=tuple(a), parent=G))
    return G


def roof_tank(P, loc, r=2.5, h=5.0, code="T-1", name="roof_tank"):
    """The building's round signature volume: a bone capsule tank standing on a framed graphite saddle, banded in steel
    blue with a mustard band and its code, a caged ladder and a railed top."""
    G = group(P, name, loc)
    framed(G, "tank_base", (0, 0, 0.4), (2 * r + 0.6, 2 * r + 0.6, 0.8), steel(), graphite(), fw=0.14)
    body = M(BONE, wear=0.25, grime=0.45, scratches=0.2, moss=0.1)
    z0 = 0.8
    quiet(K.cyl("tank_body", r, h, body, loc=(0, 0, z0 + h / 2), segs=32, bev=0.02, parent=G))
    quiet(K.sphere("tank_dome", r, body, loc=(0, 0, z0 + h), scale=(1, 1, 0.35), parent=G))
    for z in (z0 + 0.4, z0 + h - 0.3):
        quiet(K.cyl("tank_band", r + 0.04, 0.3, steel(), loc=(0, 0, z), segs=32, bev=0.01, parent=G))
    quiet(K.cyl("tank_band_m", r + 0.03, 0.9, mustard(0.3), loc=(0, 0, z0 + h * 0.62), segs=32, bev=0, parent=G))
    painted(G, code, (0, -r - 0.045, z0 + h * 0.62), 0.65, graphite())
    ladder_caged(G, (r * 0.7, -r * 0.72, z0), h + 0.5, yaw=-45)
    zt = z0 + h + r * 0.35
    quiet(K.cyl("tank_top", 0.6, 0.2, graphite(), loc=(0, 0, zt), segs=16, bev=0.01, parent=G))
    quiet(K.cyl("tank_vent", 0.12, 0.8, zinc(), loc=(0.8, 0.3, zt + 0.0), segs=10, bev=0, parent=G))
    quiet(K.torus("tank_vent_hook", 0.18, 0.06, zinc(), loc=(0.98, 0.3, zt + 0.4), rot=(90, 0, 0), arc=180, segs=12, parent=G))
    for k in range(4):
        streak(G, 0, z0 + h * 0.62 - 0.45, 1.2, 0, w=0.1, seed=k)  # rust from the band's bolts (placed on its front below)
        G.children[-1].location = (r * math.sin(k * 0.6 - 0.9) * 1.01, -r * math.cos(k * 0.6 - 0.9) * 1.01, G.children[-1].location.z)
    solid(G, "tank", (2 * r, 2 * r, h + z0), (0, 0, (h + z0) / 2))
    return G


def stack(P, loc, h=30.0, r=1.1, name="stack"):
    """A tall steel flue: a framed plinth, flanged courses tapering up, red-and-bone aviation bands, two guy rings
    with rods to anchors on the plinth's corners, a ladder with rest platforms, and a soot-black mouth. Round because
    it is a pipe. Reads at 500 m."""
    G = group(P, name, loc)
    quiet(K.box("stack_plinth", (4.0, 4.0, 0.6), concrete(), loc=(0, 0, 0.3), bev=0.05, parent=G))
    framed(G, "stack_base", (0, 0, 0.6 + 1.2), (2.9, 2.9, 2.4), steel(), graphite(), fw=0.16, screws=zinc())
    quiet(*[c for c in G.children if c.name.startswith("stack_base")])
    z0 = 3.0
    quiet(K.cyl("stack_skirt", r + 0.25, 0.6, graphite(), loc=(0, 0, z0 + 0.3), segs=28, r2=r, bev=0.02, parent=G))
    flue = M(ZINC, metal=0.45, wear=0.2, grime=0.45, scratches=0.35)
    rr = lambda z: r - 0.25 * (z - z0) / (h - z0)  # noqa: E731
    quiet(K.cyl("stack_flue", r, h - z0, flue, loc=(0, 0, (z0 + h) / 2), segs=28, r2=rr(h), bev=0.01, parent=G))
    z = z0 + 3.0
    while z < h - 1:
        quiet(K.cyl("stack_flange", rr(z) + 0.06, 0.12, graphite(), loc=(0, 0, z), segs=28, bev=0.01, parent=G))
        z += 3.0
    for k, (zz, col) in enumerate(((h - 4.2, SIGNAL), (h - 3.0, BONE), (h - 1.8, SIGNAL))):
        quiet(K.cyl("stack_band", rr(zz) + 0.012, 1.2, M(col, wear=0.2, grime=0.3), loc=(0, 0, zz), segs=28, r2=rr(zz + 1.2) + 0.012, bev=0, parent=G))
    quiet(K.cyl("stack_soot", rr(h) + 0.015, 0.9, K.mat("soot", "#221f1c", rough=0.95), loc=(0, 0, h - 0.45), segs=28, bev=0, parent=G))
    quiet(K.cyl("stack_mouth", rr(h) - 0.08, 0.05, K.mat("soot", "#221f1c", rough=0.95), loc=(0, 0, h - 0.02), segs=24, bev=0, parent=G))
    quiet(K.cyl("stack_lip", rr(h) + 0.08, 0.2, graphite(), loc=(0, 0, h + 0.05), segs=28, bev=0.02, parent=G))
    for zg in (h * 0.45, h * 0.8):
        quiet(K.cyl("guy_ring", rr(zg) + 0.1, 0.25, graphite(), loc=(0, 0, zg), segs=28, bev=0.02, parent=G))
        for k in range(3):
            a = 2 * math.pi * k / 3 + 0.5
            far = 9.0 if zg > h * 0.5 else 7.0
            quiet(rod(G, "guy", (rr(zg) * math.cos(a), rr(zg) * math.sin(a), zg), (far * math.cos(a), far * math.sin(a), 0.6), 0.03, zinc(), segs=5))
            quiet(K.box("guy_anchor", (0.6, 0.6, 0.5), concrete(), loc=(far * math.cos(a), far * math.sin(a), 0.25), bev=0.03, parent=G))
    # ladder up the -y side and rest platforms
    ladder_caged(G, (0, -r - 0.35, z0), h - z0 - 0.5)
    for zp in (h * 0.45 - 1.0, h * 0.8 - 1.0):
        quiet(K.cyl("stack_platform", rr(zp) + 1.0, 0.12, gunmetal(), loc=(0, 0, zp), segs=28, bev=0.01, parent=G))
        quiet(K.cyl("stack_platform_edge", rr(zp) + 1.02, 0.12, mustard(0.5), loc=(0, 0, zp + 0.08), segs=28, bev=0, parent=G))
        quiet(K.torus("stack_platform_rail", rr(zp) + 0.95, 0.03, mustard(0.4), loc=(0, 0, zp + 1.1), segs=24, parent=G))
        for k in range(10):
            a = 2 * math.pi * k / 10
            quiet(K.box("stack_rail_post", (0.05, 0.05, 1.1), steel(), loc=((rr(zp) + 0.95) * math.cos(a), (rr(zp) + 0.95) * math.sin(a), zp + 0.55), bev=0, parent=G))
            if k % 2 == 0:
                quiet(beam(G, "stack_bracket", (rr(zp) * math.cos(a), rr(zp) * math.sin(a), zp - 1.0),
                           ((rr(zp) + 0.9) * math.cos(a), (rr(zp) + 0.9) * math.sin(a), zp - 0.06), 0.08, steel()))
    quiet(K.cyl("stack_lamp", 0.16, 0.25, glow("#ff4a3a", 6), loc=(rr(h) * 0.7, 0, h + 0.3), segs=10, bev=0, parent=G))
    solid(G, "stack", (2 * r, 2 * r, h), (0, 0, h / 2))
    solid(G, "stack_base", (2.9, 2.9, 3.0), (0, 0, 1.5))
    return G


def _lattice(G, name, a, b, w, chord, lace, mat, lace_mat, n_side=4):
    """A square lattice boom or mast from a to b: four chords and zig-zag lacing on every face."""
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    u = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
    s1 = d.cross(u).normalized()
    s2 = d.cross(s1).normalized()
    cs = [(s1 * i + s2 * j) * (w / 2) for i, j in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    for c in cs:
        quiet(beam(G, f"{name}_chord", a + c, b + c, chord, mat))
    L = (b - a).length
    n = max(1, int(L / w))
    for k in range(n):
        p0, p1 = a + d * (L * k / n), a + d * (L * (k + 1) / n)
        for f in range(4):
            c0, c1 = cs[f], cs[(f + 1) % 4]
            q0, q1 = (p0 + c0, p1 + c1) if k % 2 == 0 else (p0 + c1, p1 + c0)
            quiet(beam(G, f"{name}_lace", q0, q1, lace, lace_mat))
        for f in range(4):
            quiet(beam(G, f"{name}_strut", p1 + cs[f], p1 + cs[(f + 1) % 4], lace, lace_mat))


def tower_crane(P, loc, mast=34.0, jib=30.0, counter=10.0, yaw=0.0, hook=12.0, name="tower_crane"):
    """A colony tower crane standing in the yard: a mustard lattice mast on a concrete footing, a slewing deck with a
    bone operator cab, a lattice jib with its trolley and hook block, a counter-jib with graphite counterweights, the
    A-frame and tie bars. Square lattice, so not round anywhere but the sheaves. Reads at 500 m."""
    G = group(P, name, loc, yaw)
    mm, gra = mustard(0.35), graphite()
    quiet(K.box("crane_footing", (6.0, 6.0, 1.0), concrete(), loc=(0, 0, 0.5), bev=0.06, parent=G))
    for sx in (-1, 1):
        for sy in (-1, 1):
            quiet(K.box("crane_foot", (0.6, 0.6, 0.4), gra, loc=(sx * 1.0, sy * 1.0, 1.2), bev=0.03, parent=G))
    _lattice(G, "mast", (0, 0, 1.0), (0, 0, mast), 2.0, 0.18, 0.07, mm, mm)
    # slewing deck and cab
    zd = mast
    quiet(K.cyl("slew_ring", 1.4, 0.5, gra, loc=(0, 0, zd + 0.25), segs=24, bev=0.03, parent=G))
    quiet(K.box("slew_deck", (3.0, 3.0, 0.5), steel(), loc=(0, 0, zd + 0.75), bev=0.04, parent=G))
    framed(G, "crane_cab", (1.2, -2.1, zd + 0.3), (1.8, 1.6, 2.0), M(BONE, wear=0.35, grime=0.3), steel(), fw=0.1)
    quiet(*[c for c in G.children if c.name.startswith("crane_cab")])
    quiet(K.box("cab_glass", (1.6, 0.04, 1.0), glass(), loc=(1.2, -2.92, zd + 0.6), bev=0, parent=G))
    quiet(K.box("cab_glass_side", (0.04, 1.3, 1.0), glass(), loc=(2.12, -2.1, zd + 0.6), bev=0, parent=G))
    zj = zd + 1.0
    # jib (+x) and counter-jib (-x), triangular in section read as square lattice for strength
    _lattice(G, "jib", (1.0, 0, zj + 0.9), (jib, 0, zj + 0.9), 1.6, 0.14, 0.06, mm, mm)
    _lattice(G, "cjib", (-1.0, 0, zj + 0.9), (-counter, 0, zj + 0.9), 1.6, 0.16, 0.06, mm, mm)
    quiet(K.box("cjib_walk", (counter - 1.0, 1.6, 0.06), gunmetal(), loc=(-(counter + 1.0) / 2, 0, zj + 0.1), bev=0, parent=G))
    for k in range(3):
        quiet(K.box("counterweight", (1.0, 2.0, 2.2), M(CONCRETE, wear=0.2, grime=0.4), loc=(-counter + 1.0 + k * 1.05, 0, zj - 0.6), bev=0.04, parent=G))
    hazard_band(G, (-counter + 2.05, -1.01, zj - 1.6), 3.1, 0.5, name="cw_haz")
    # A-frame and ties
    apex = Vector((0, 0, zj + 7.0))
    for sy in (-1, 1):
        quiet(beam(G, "aframe", (1.0, sy * 0.8, zj + 0.1), apex + Vector((0, sy * 0.15, 0)), 0.22, mm))
        quiet(beam(G, "aframe", (-1.0, sy * 0.8, zj + 0.1), apex + Vector((0, sy * 0.15, 0)), 0.22, mm))
        quiet(rod(G, "tie", apex + Vector((0, sy * 0.15, 0)), (jib * 0.62, sy * 0.7, zj + 1.7), 0.05, graphite(), segs=6))
        quiet(rod(G, "tie", apex + Vector((0, sy * 0.15, 0)), (-counter + 0.5, sy * 0.7, zj + 1.7), 0.05, graphite(), segs=6))
    quiet(K.cyl("apex_sheave", 0.35, 0.5, gra, loc=tuple(apex), rot=(90, 0, 0), segs=16, bev=0.02, parent=G))
    quiet(K.cyl("crane_lamp", 0.15, 0.25, glow("#ff4a3a", 6), loc=tuple(apex + Vector((0, 0, 0.5))), segs=10, bev=0, parent=G))
    # trolley, ropes and hook block
    tx = jib * 0.7
    quiet(K.box("trolley", (1.4, 1.8, 0.5), gra, loc=(tx, 0, zj + 0.0), bev=0.04, parent=G))
    zh = zj - hook
    for sy in (-0.25, 0.25):
        quiet(rod(G, "hoist_rope", (tx, sy, zj - 0.2), (tx, sy, zh + 0.8), 0.025, graphite(), segs=5))
    framed(G, "hook_block", (tx, 0, zh + 0.4), (0.7, 0.5, 0.9), mm, gra, fw=0.06)
    quiet(*[c for c in G.children if c.name.startswith("hook_block")])
    hazard_band(G, (tx, -0.26, zh + 0.1), 0.7, 0.3, name="hook_haz")
    quiet(K.torus("hook", 0.25, 0.07, gra, loc=(tx, 0, zh - 0.25), rot=(90, 0, 0), arc=270, segs=16, parent=G))
    # hoist drum and an access ladder up the mast's -y face
    quiet(K.cyl("hoist_drum", 0.5, 1.4, gra, loc=(-3.0, 0, zj + 0.7), rot=(90, 0, 0), segs=16, bev=0.03, parent=G))
    solid(G, "crane_mast", (2.0, 2.0, mast), (0, 0, mast / 2))
    solid(G, "crane_footing", (6.0, 6.0, 1.0), (0, 0, 0.5))
    return G


# ================================================================== yard pieces no prop covers yet
def floodlight_mast(P, loc, h=12.0, yaw=0.0, name="floodlight_mast", lit=True):
    """A yard floodlight mast: a square steel-blue pole on a framed footing, a graphite head frame with four
    floodlights tilted down onto the yard (towards -y), a service box and cable conduit."""
    G = group(P, name, loc, yaw)
    quiet(K.box("fl_footing", (1.0, 1.0, 0.5), concrete(), loc=(0, 0, 0.25), bev=0.04, parent=G))
    quiet(K.box("fl_pole", (0.3, 0.3, h), steel(), loc=(0, 0, h / 2 + 0.5), bev=0.03, parent=G))
    hazard_band(G, (0, -0.16, 0.6), 0.3, 1.2, name="fl_haz")
    framed(G, "fl_box", (0, -0.3, 1.6), (0.5, 0.25, 0.7), M(BONE, wear=0.35), graphite(), fw=0.04)
    quiet(*[c for c in G.children if c.name.startswith("fl_box")])
    quiet(K.box("fl_conduit", (0.06, 0.06, h - 1.2), graphite(), loc=(0.2, 0, (h - 1.2) / 2 + 1.2), bev=0, parent=G))
    zt = h + 0.5
    quiet(K.box("fl_frame", (2.4, 0.16, 0.16), graphite(), loc=(0, -0.2, zt), bev=0.02, parent=G))
    quiet(K.box("fl_frame_b", (2.4, 0.16, 0.16), graphite(), loc=(0, -0.2, zt + 0.8), bev=0.02, parent=G))
    for k, x in enumerate((-0.9, -0.3, 0.3, 0.9)):
        z = zt + (0.4 if k % 2 else 0.4)
        quiet(K.box("floodlight", (0.55, 0.3, 0.45), graphite(), loc=(x, -0.45, z), rot=(-35, 0, (k - 1.5) * 6), bev=0.04, parent=G))
        quiet(K.box("fl_lens", (0.45, 0.02, 0.35), glow(WARM, 14) if lit else glass(), loc=(x, -0.62, z - 0.1), rot=(-35, 0, (k - 1.5) * 6), bev=0, parent=G))
    if lit:
        K.point("flood", (0, -1.2, zt - 0.5), WARM, 900, 0.4).parent = G
    solid(G, "fl_pole", (0.3, 0.3, h), (0, 0, h / 2 + 0.5))
    return G


def mesh_fence(P, a, b, h=2.25, name="fence", gate=None, panel=2.5, lite=False):
    """A welded-mesh yard fence from a to b (ground points): square steel posts every 2.5 m, a top and bottom rail,
    a wire grid and a strand of barbed wire on raked arms. gate=(t0, t1) leaves a gap between those fractions with
    a sliding gate pushed back beside it. lite: a coarser grid and unbevelled posts, for long runs (a yard)."""
    G = group(P, name)
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    d = (b - a).normalized()
    L = (b - a).length
    yaw = math.degrees(math.atan2(d.y, d.x))
    n = max(1, round(L / panel))
    wire = zinc()
    side = Vector((-d.y, d.x, 0))
    for k in range(n + 1):
        if gate and gate[0] + 1e-6 < k / n < gate[1] - 1e-6:
            continue
        p = a + d * (L * k / n)
        quiet(K.box("fence_post", (0.1, 0.1, h + 0.2), steel(), loc=tuple(p + Vector((0, 0, (h + 0.2) / 2))), rot=(0, 0, yaw), bev=0 if lite else 0.01, parent=G))
        quiet(beam(G, "fence_arm", p + Vector((0, 0, h + 0.15)), p + Vector((0, 0, h + 0.55)) - side * 0.3, 0.04, graphite()))
    for k in range(n):
        t0, t1 = k / n, (k + 1) / n
        if gate and gate[0] <= t0 + 1e-6 and t1 <= gate[1] + 1e-6:
            continue
        p0, p1 = a + d * (L * t0), a + d * (L * t1)
        for z in (0.15, h):
            quiet(rod(G, "fence_rail", p0 + Vector((0, 0, z)), p1 + Vector((0, 0, z)), 0.025, steel(), segs=4 if lite else 6))
        m, hm = (5, 5) if lite else (10, 9)
        for j in range(1, m):
            q = p0 + (p1 - p0) * (j / m)
            quiet(K.box("fence_wire_v", (0.012, 0.012, h - 0.2), wire, loc=tuple(q + Vector((0, 0, h / 2 + 0.05))), rot=(0, 0, yaw), bev=0, parent=G))
        for j in range(1, hm):
            z = 0.15 + (h - 0.15) * j / hm
            quiet(rod(G, "fence_wire_h", p0 + Vector((0, 0, z)), p1 + Vector((0, 0, z)), 0.007 if not lite else 0.01, wire, segs=3 if lite else 4))
        quiet(rod(G, "barb", p0 + Vector((0, 0, h + 0.5)) - side * 0.27, p1 + Vector((0, 0, h + 0.5)) - side * 0.27, 0.012, graphite(), segs=4))
        solid(G, "fence", (L / n, 0.1, h), tuple((p0 + p1) / 2 + Vector((0, 0, h / 2))), rot=(0, 0, yaw))
    if gate:
        # the sliding gate, pushed open along the fence's outer side
        g0 = a + d * (L * gate[0])
        gl = L * (gate[1] - gate[0])
        q0, q1 = g0 - d * gl * 0.95 - side * 0.25, g0 - d * 0.05 - side * 0.25
        for z in (0.2, h * 0.5, h - 0.05):
            quiet(rod(G, "gate_rail", q0 + Vector((0, 0, z)), q1 + Vector((0, 0, z)), 0.04, steel(), segs=6))
        for j in range(int(gl / 0.6) + 1):
            q = q0 + (q1 - q0) * (j / max(1, int(gl / 0.6)))
            quiet(K.box("gate_bar", (0.05, 0.05, h - 0.2), steel(), loc=tuple(q + Vector((0, 0, h / 2 + 0.05))), rot=(0, 0, yaw), bev=0, parent=G))
        hazard_band(G, tuple(q1 - side * 0.05 + Vector((0, 0, h * 0.45))), 0.6, 0.25, yaw=yaw, name="gate_haz")
    return G


def intake_head(G, x, z, y=0.0):
    """Where the power line comes in: a graphite bracket with three insulators, a meter box and conduit down."""
    quiet(K.box("intake_arm", (1.4, 0.8, 0.12), graphite(), loc=(x, y - 0.4, z), bev=0.02, parent=G))
    for k in (-0.5, 0, 0.5):
        quiet(K.cyl("intake_ins", 0.07, 0.3, M("#7a8a8a", rough=0.3, wear=0), loc=(x + k, y - 0.7, z + 0.2), segs=10, bev=0.01, parent=G))
    framed(G, "intake_meter", (x, y - 0.2, z - 2.2), (0.9, 0.35, 1.2), M(BONE, wear=0.4), graphite(), fw=0.05)
    quiet(K.box("intake_conduit", (0.1, 0.1, 1.6), graphite(), loc=(x, y - 0.15, z - 0.8), bev=0, parent=G))
    quiet(K.box("intake_plate", (0.4, 0.02, 0.3), mustard(0.2), loc=(x, y - 0.385, z - 1.95), bev=0, parent=G))


# ================================================================== the colony sites' modules (colony_sites.py)
def porthole(G, x, z, r=0.5, y=0.0, plate=1.5):
    """A round window on a flat bone plate let into the cladding (wall frame, facing -y): a steel-blue frame round the
    plate, a bolted graphite ring, the glass and a small rain hood over it."""
    quiet(K.box("port_plate", (plate, 0.06, plate), bone(1), loc=(x, y - 0.03, z), bev=0.01, parent=G))
    for s in (-1, 1):
        quiet(K.box("port_frame", (0.1, 0.12, plate + 0.1), steel(), loc=(x + s * plate / 2, y - 0.06, z), bev=0, parent=G))
        quiet(K.box("port_frame", (plate + 0.1, 0.12, 0.1), steel(), loc=(x, y - 0.06, z + s * plate / 2), bev=0, parent=G))
    quiet(K.torus("port_ring", r, 0.07, graphite(), loc=(x, y - 0.1, z), rot=(90, 0, 0), segs=24, parent=G))
    quiet(K.cyl("port_glass", r - 0.02, 0.02, glass(), loc=(x, y - 0.07, z), rot=(90, 0, 0), segs=24, bev=0, parent=G))
    for k in range(8):
        a = k * math.pi / 4 + math.pi / 8
        quiet(K.box("port_bolt", (0.06, 0.05, 0.06), zinc(), loc=(x + r * math.cos(a), y - 0.17, z + r * math.sin(a)), bev=0, parent=G))
    quiet(K.box("port_hood", (2 * r + 0.3, 0.25, 0.06), graphite(), loc=(x, y - 0.18, z + r + 0.18), rot=(-15, 0, 0), bev=0.01, parent=G))
    return G


def door_hole(x, z, w=1.25, h=2.5):
    """The opening an open doorway (doorway()) needs in a wall: pass it in the wall's holes. A doorway on the ground
    floor (z at the slab top) is cut through the plinth to the ground."""
    return (x - w / 2 - 0.15, x + w / 2 + 0.15, 0.0 if z <= SLAB + 1e-3 else z, z + h + 0.15)


def doorway(G, x, z, y=0.0, w=1.25, h=2.5, swing=1, lamp=True, name="doorway"):
    """An open walk-in doorway in a wall (wall frame, outside -y; its opening is door_hole()): graphite jambs and head
    through the wall, a chequer sill, the steel door swung right back flat against the wall outside (to +x, or -x
    with swing=-1) and a warm lamp over it."""
    D = group(G, name)
    gra = graphite()
    for sx in (-1, 1):
        quiet(K.box("dw_jamb", (0.14, 0.5, h + 0.14), gra, loc=(x + sx * (w / 2 + 0.07), y + 0.05, z + h / 2 + 0.07), bev=0.015, parent=D))
    quiet(K.box("dw_head", (w + 0.42, 0.5, 0.16), gra, loc=(x, y + 0.05, z + h + 0.15), bev=0.015, parent=D))
    quiet(K.box("dw_sill", (w + 0.2, 0.5, 0.03), gunmetal(), loc=(x, y + 0.05, z + 0.015), bev=0, parent=D))
    hx = x + swing * (w + 0.16)
    quiet(K.box("dw_leaf", (w, 0.06, h), steel(), loc=(hx, y - 0.17, z + h / 2), bev=0.02, parent=D))
    quiet(K.box("dw_kick", (w - 0.1, 0.02, 0.3), zinc(), loc=(hx, y - 0.21, z + 0.2), bev=0, parent=D))
    quiet(K.box("dw_vision", (0.3, 0.02, 0.5), glass(), loc=(hx + swing * 0.2, y - 0.21, z + 1.6), bev=0, parent=D))
    quiet(K.box("dw_bar", (0.7, 0.06, 0.06), zinc(), loc=(hx - swing * 0.1, y - 0.23, z + 1.05), bev=0.01, parent=D))
    if lamp:
        quiet(K.box("dw_lamp", (0.5, 0.22, 0.14), gra, loc=(x, y - 0.2, z + h + 0.48), bev=0.03, parent=D))
        quiet(K.box("dw_lens", (0.42, 0.16, 0.02), glow(WARM, 10), loc=(x, y - 0.22, z + h + 0.405), bev=0, parent=D))
    return D


def prop(P, build, loc, yaw=0.0, collide=True, pad=0.0):
    """Places a world prop (a props*.py builder) in a structure, drawn only. collide=True gives it one collision box,
    its bounding box less pad on each side; (size, centre) gives that box in the prop's frame; False gives none."""
    root = build()
    root.parent = P
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(yaw))
    quiet_all(root)
    if collide is True:
        bpy.context.view_layer.update()
        inv = root.matrix_world.inverted()
        pts = [inv @ (ob.matrix_world @ Vector(c)) for ob in root.children_recursive if ob.type == "MESH" for c in ob.bound_box]
        lo = Vector((min(p.x for p in pts) + pad, min(p.y for p in pts) + pad, max(0.0, min(p.z for p in pts))))
        hi = Vector((max(p.x for p in pts) - pad, max(p.y for p in pts) - pad, max(p.z for p in pts)))
        solid(root, "prop", tuple(hi - lo), tuple((hi + lo) / 2))
    elif collide:
        size, off = collide
        solid(root, "prop", size, off)
    return root


def face(G, name, pts, mat):
    """One flat face through 3D points (glass panes on a curve), drawn only."""
    bm = bmesh.new()
    bm.faces.new([bm.verts.new(p) for p in pts])
    ob = K.from_bm(name, bm, mat)
    ob.parent = G
    return quiet(ob)


# ---------------------------------------------------------------- silos, the stair tower, the gallery
def silo(P, loc, r=4.0, h=16.0, rise=0.6, code="S-1", seed=1, gap=None, ladder=-90.0, name="silo"):
    """A colony grain silo standing on loc: a bone drum in welded 2.5 m courses held by steel-blue stiffener posts and ring
    belts on a concrete ring footing, a low steel-blue cone roof you walk on (rise metres to an inlet collar) inside a
    railing, a roof hatch and vent, a caged ladder up the side at angle ladder (degrees from +x; None: none), a discharge chute at
    the foot, rust runs from the belts and a mustard band with its code. gap=(angle, half, d) leaves the railing open
    towards angle over half degrees each way, for a gallery door, with closure rails from the gap's ends to the plane
    d metres out that way (the gallery wall)."""
    G = group(P, name, loc)
    rnd = random.Random(seed)
    K.cyl("silo_footing", r + 0.35, 0.5, concrete(), loc=(0, 0, 0.25), segs=32, bev=0.03, parent=G)
    K.cyl("silo_body", r, h - 0.5, bone(seed), loc=(0, 0, 0.5 + (h - 0.5) / 2), segs=32, bev=0, parent=G)
    for z in [0.5 + 2.5 * k for k in range(1, int((h - 0.5) / 2.5))]:
        bm = bmesh.new()      # an open hoop: its ends are never seen
        bmesh.ops.create_cone(bm, cap_ends=False, segments=32, radius1=r + 0.008, radius2=r + 0.008, depth=0.03)
        seam = K.from_bm("silo_seam", bm, M("#c7bfa8", wear=0, grime=0.3, scratches=0))
        seam.parent, seam.location = G, (0, 0, z)
        quiet(seam)
    belts = [0.65] + [float(z) for z in range(4, int(h), 4)] + [h - 0.15]
    for z in belts:
        quiet(K.cyl("silo_belt", r + 0.06, 0.3, steel(), loc=(0, 0, z), segs=32, bev=0, parent=G))
    for k in range(12):
        a = 2 * math.pi * k / 12 + math.pi / 12
        quiet(K.box("silo_stiffener", (0.14, 0.24, h - 0.5), steel(), loc=((r + 0.05) * math.cos(a), (r + 0.05) * math.sin(a), 0.5 + (h - 0.5) / 2),
                    rot=(0, 0, math.degrees(a)), bev=0, parent=G))
    zb = round(h * 0.62)
    quiet(K.cyl("silo_band", r + 0.03, 1.3, mustard(0.35), loc=(0, 0, zb), segs=32, bev=0, parent=G))
    import props_industry as PI  # noqa: PLC0415
    for a0 in (-math.pi / 2, math.pi / 2):
        PI.ring_text(G, code, r + 0.04, zb, a0, 0.85, graphite(), spacing=0.7, extrude=0.0)
    for c in [c for c in G.children if c.type == "FONT"]:
        quiet(c)
    for k in range(5):
        a = rnd.uniform(0, 2 * math.pi)
        S = group(G, "silo_streak", ((r + 0.075) * math.cos(a), (r + 0.075) * math.sin(a), 0), math.degrees(a) + 90)
        streak(S, 0.0, rnd.choice(belts[1:-1] or [h / 2]) - 0.16, rnd.uniform(0.8, 1.8), 0.0, w=0.12, seed=seed * 7 + k)
    # the roof: a low cone, an eave rim, the inlet collar where the spout comes in, a hatch and a vent
    K.cyl("silo_roof", r + 0.12, rise, steel(), loc=(0, 0, h + rise / 2), r2=0.55, segs=32, bev=0, parent=G)
    quiet(K.cyl("silo_eave", r + 0.16, 0.18, graphite(), loc=(0, 0, h - 0.02), segs=32, bev=0.01, parent=G))
    K.cyl("silo_collar", 0.55, 0.45, graphite(), loc=(0, 0, h + rise + 0.2), r2=0.45, segs=16, bev=0.01, parent=G)
    away = math.radians(gap[0] + 180) if gap else 0.0

    def zr(rr):
        return h + rise * ((r + 0.12) - rr) / ((r + 0.12) - 0.55)
    hx, hy = 0.45 * r * math.cos(away + 0.6), 0.45 * r * math.sin(away + 0.6)
    roof_hatch(G, (hx, hy, zr(0.45 * r) - 0.05), yaw=math.degrees(away))
    solid(G, "hatch", (1.3, 1.3, 0.7), (hx, hy, zr(0.45 * r) + 0.3), rot=(0, 0, math.degrees(away)))
    vx, vy = 0.5 * r * math.cos(away - 0.9), 0.5 * r * math.sin(away - 0.9)
    mushroom_vent(G, (vx, vy, zr(0.5 * r) - 0.05), r=0.3, h=0.6)
    # the railing round the roof's edge, open towards a gallery
    rr = r - 0.15
    n = 20
    ends = []
    prev = None
    for k in range(n):
        a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
        if gap:
            off = (math.degrees((a0 + a1) / 2) - gap[0] + 180) % 360 - 180
            if abs(off) < gap[1]:
                continue
            for a in (a0, a1):
                if abs((math.degrees(a) - gap[0] + 180) % 360 - 180) <= gap[1] + 360 / n / 2 + 1e-6:
                    ends.append(a)
        railing(G, (rr * math.cos(a0), rr * math.sin(a0), zr(rr)), (rr * math.cos(a1), rr * math.sin(a1), zr(rr)), posts=1.3,
                first=prev != k - 1, lite=True)
        prev = k
    if gap:
        u = Vector((math.cos(math.radians(gap[0])), math.sin(math.radians(gap[0])), 0))
        for a in ends:
            p = Vector((rr * math.cos(a), rr * math.sin(a), zr(rr)))
            q = p + u * (gap[2] - p.dot(u) - 0.12)
            if (q - p).length > 0.1:
                railing(G, p, q, posts=1.0)
    # a caged ladder up the side (decor: players climb the stair tower), the discharge chute at the foot
    la = math.radians(ladder if ladder is not None else -90.0)
    if ladder is not None:
        ladder_caged(G, ((r + 0.3) * math.cos(la), (r + 0.3) * math.sin(la), 0.5), h - 0.3, yaw=ladder + 90)
    ca = la + 0.7
    quiet(rod(G, "chute", ((r - 0.2) * math.cos(ca), (r - 0.2) * math.sin(ca), 1.2), ((r + 1.0) * math.cos(ca), (r + 1.0) * math.sin(ca), 0.7), 0.22, steel(), segs=10))
    quiet(K.box("chute_gate", (0.6, 0.6, 0.5), graphite(), loc=((r + 0.35) * math.cos(ca), (r + 0.35) * math.sin(ca), 1.0), rot=(0, 0, math.degrees(ca)), bev=0.02, parent=G))
    return G


def stair_tower(P, loc, floors, S=6.0, lane=1.5, yaw=0.0, exits=(), head=3.0, code="E-1", seed=1, name="stair_tower"):
    """A Colony Works stair tower, the elevator leg's housing: a framed box S square in the kit's walls, a square
    spiral stair inside it (along each wall a flight of four 0.25 m risers, a landing in each corner: a storey a
    turn) round the leg's steel-blue casing in the middle, from a walk-in doorway in the left wall up to the deck at
    the top (its top at 4 * floors + 0.3, the parapet round it) and the leg's head house on the casing. The top three
    flights rise through an open well (the deck covers the front lane and the right one only up to its exits). exits: ('front' or 'right', along) openings in the walls at the deck for
    galleries, 'along' metres from the wall's middle (seen from outside). Local frame: the corner at loc, the tower on
    +x +y of it, its front facing -y. Returns the group, its walls and the deck's height."""
    G = group(P, name, loc, yaw)
    H = STOREY * floors + SLAB
    a, inner = lane, 0.2
    c0, c1 = a, S - a
    holes = {"front": [], "right": [], "back": [], "left": [door_hole(0.0, SLAB)]}
    for side, u in exits:
        holes[side].append((u - 1.0, u + 1.0, H, H + 2.45))
    shift = {"front": 0, "right": 1, "back": 1, "left": 0}

    def plan(side):
        def p(b, f):
            if side == "left" and f == 0:
                return "plain"
            if (f + shift[side]) % 2 == 0:
                return "window"
            return "louvre" if f % 3 == 2 else "plain"
        return p
    walls = {}
    for k, (side, wl, wy) in enumerate((("front", (S / 2, 0, 0), 0), ("right", (S, S / 2, 0), 90), ("back", (S / 2, S, 0), 180), ("left", (0, S / 2, 0), -90))):
        walls[side] = wall(G, f"tower_{side}", S, floors, loc=wl, yaw=wy, bays=[S], plan=plan(side), holes=holes[side], seed=seed + k)
    doorway(walls["left"], 0.0, SLAB)
    for x, y in ((0, 0), (S, 0), (S, S), (0, S)):
        corner_post(G, (x, y, 0), H + PARAPET)
    downpipe(walls["back"], (S / 2 - 0.9, 0, 0), H + PARAPET)
    K.box("tower_slab", (S, S, SLAB), concrete(), loc=(S / 2, S / 2, SLAB / 2), bev=0.02, parent=G)
    # the leg's casing up the middle
    framed(G, "leg_casing", (S / 2, S / 2, (SLAB + H) / 2), (c1 - c0, c1 - c0, H - SLAB), steel(), graphite(), fw=0.14)
    solid(G, "leg_casing", (c1 - c0, c1 - c0, H - SLAB), (S / 2, S / 2, (SLAB + H) / 2))
    for z in range(4, int(H), 4):
        quiet(K.box("leg_band", (c1 - c0 + 0.06, c1 - c0 + 0.06, 0.25), graphite(), loc=(S / 2, S / 2, z + 0.3), bev=0.01, parent=G))
    hazard_band(G, (S / 2, c0 - 0.01, SLAB), c1 - c0 - 0.3, 0.5, name="leg_haz")
    # the spiral: flight j rises a quarter storey along side j % 4 from one corner landing to the next
    m = (inner + a) / 2
    corners = [(inner, a, inner, a), (S - a, S - inner, inner, a), (S - a, S - inner, S - a, S - inner), (inner, a, S - a, S - inner)]
    n = 4 * floors
    for j in range(n):
        side, z0 = j % 4, SLAB + j * STOREY / 4
        A, B = (((c0, m), (c1, m)), ((S - m, c0), (S - m, c1)), ((c1, S - m), (c0, S - m)), ((m, c1), (m, c0)))[side]
        steel_flight(G, (A[0], A[1], z0), (B[0], B[1], z0 + STOREY / 4), a - inner, name=f"tw_flight{j}", rails=(False, False))
        if j < n - 1:
            x0, x1, y0, y1 = corners[(side + 1) % 4]
            landing(G, x0, x1, y0, y1, z0 + STOREY / 4)
    # the deck: the front lane, and the right lane as far as its exits (the rest of the right lane, the back and the
    # left are the well the last three flights rise through; a thin deck keeps 1.95 m over the flight under it)
    t = 0.15
    landing(G, inner, S - inner, inner, a, H, t=t)
    yr = max([S / 2 + u + 1.05 for side, u in exits if side == "right"], default=a)
    assert 3 * STOREY / 4 - t - max(0.0, yr - c0) / (c1 - c0) * STOREY / 4 >= 1.95, "a right exit too far back over the stair"
    if yr > a:
        landing(G, S - a, S - inner, a - 0.1, yr, H, t=t)     # over the front lane's edge: no crack between them
    railing(G, (S - a + 0.05, yr + 0.06, H), (S - inner - 0.05, yr + 0.06, H))
    # the head house on the casing: the leg's head, a louvre, its code, a vent and a beacon
    hs = c1 - c0
    framed(G, "leg_head", (S / 2, S / 2, H + head / 2), (hs, hs, head), bone(2), steel(), fw=0.14)
    solid(G, "leg_head", (hs, hs, head), (S / 2, S / 2, H + head / 2))
    quiet(K.box("leg_head_roof", (hs + 0.3, hs + 0.3, 0.2), graphite(), loc=(S / 2, S / 2, H + head + 0.1), bev=0.02, parent=G))
    louvre(group(G, "leg_head_front", (S / 2, c0, 0)), 0.0, H + 0.9, 1.5, 1.25, y=-0.02)
    F = group(G, "leg_head_right", (c1, S / 2, 0), 90)
    quiet(K.box("code_plate", (2.0, 0.04, 0.8), mustard(0.3), loc=(0, -0.03, H + head - 0.8), bev=0, parent=F))
    painted(F, code, (0, -0.06, H + head - 0.8), 0.55, graphite())
    mushroom_vent(G, (S / 2 - 0.5, S / 2 + 0.4, H + head + 0.2), r=0.3, h=0.6)
    quiet(K.box("tw_mast", (0.08, 0.08, 3.0), graphite(), loc=(S / 2 + 0.8, S / 2 + 0.8, H + head + 1.7), bev=0, parent=G))
    quiet(K.cyl("tw_beacon", 0.14, 0.22, glow("#ff4a3a", 6), loc=(S / 2 + 0.8, S / 2 + 0.8, H + head + 3.3), segs=10, bev=0, parent=G))
    return G, walls, H


def gallery(P, x0, x1, yc, z, w=2.5, h=3.0, doors=(), ends=(False, True), trestles=(), seed=1, name="gallery"):
    """An enclosed conveyor gallery along x from x0 to x1 at y = yc, its floor top at z, w wide and h tall: steel-blue
    posts every 2.5 m or less with bone panels and a window strip between (some panes gone), steel belts, a graphite
    roof with a ridge vent, a chequer floor, and the belt hung from the roof down the middle (its underside 2.2 m up,
    so you walk under it). doors: (x, side) walk-out openings 1.25 m wide in the -y (side -1) or +y (+1) wall; ends:
    whether the x0 and x1 ends are walled (an open end joins the tower); trestles: x of lattice legs to the ground."""
    G = group(P, name)
    rnd = random.Random(seed)
    L, cx, hw = x1 - x0, (x0 + x1) / 2, w / 2
    quiet(K.box("gal_floor", (L, w + 0.1, 0.25), graphite(), loc=(cx, yc, z - 0.125), bev=0.02, parent=G))
    quiet(K.box("gal_deck", (L - 0.1, w - 0.3, 0.02), gunmetal(), loc=(cx, yc, z + 0.01), bev=0, parent=G))
    solid(G, "gal_floor", (L, w, 0.3), (cx, yc, z - 0.15))
    quiet(K.box("gal_roof", (L + 0.2, w + 0.5, 0.2), graphite(), loc=(cx, yc, z + h + 0.1), bev=0.02, parent=G))
    quiet(K.box("gal_ridge", (L - 0.4, 0.7, 0.3), zinc(), loc=(cx, yc, z + h + 0.35), bev=0.02, parent=G))
    solid(G, "gal_roof", (L, w, 0.2), (cx, yc, z + h + 0.1))
    # the belt down the middle on hangers from the roof
    zb = z + 2.25
    quiet(K.box("gal_belt_frame", (L - 0.3, 0.9, 0.18), steel(), loc=(cx, yc, zb + 0.09), bev=0.01, parent=G))
    quiet(K.box("gal_belt", (L - 0.4, 0.7, 0.03), K.mat("belt", "#232527", rough=0.85, grime=0.3), loc=(cx, yc, zb + 0.2), bev=0, parent=G))
    k = 0.0
    while k < L - 0.5:
        quiet(K.box("gal_hanger", (0.06, 0.95, 0.06), graphite(), loc=(x0 + 0.5 + k, yc, zb + 0.4), bev=0, parent=G))
        for s in (-1, 1):
            quiet(K.box("gal_hanger_rod", (0.04, 0.04, h - 2.65), graphite(), loc=(x0 + 0.5 + k, yc + s * 0.45, (zb + 0.4 + z + h) / 2), bev=0, parent=G))
        k += 3.0
    for s in (-1, 1):
        y = yc + s * hw
        cuts = sorted((dx - 0.75, dx + 0.75) for dx, side in doors if side == s)
        spans, e = [], x0
        for c0, c1 in cuts:
            spans.append((e, c0))
            e = c1
        spans.append((e, x1))
        for a0, a1 in spans:
            if a1 - a0 < 0.05:
                continue
            nb = max(1, math.ceil((a1 - a0) / 2.5))
            bw = (a1 - a0) / nb
            for b in range(nb + 1):
                quiet(K.box("gal_post", (0.18, 0.22, h), steel(), loc=(a0 + b * bw, y, z + h / 2), bev=0, parent=G))
            for b in range(nb):
                xm = a0 + b * bw + bw / 2
                quiet(K.box("gal_panel", (bw - 0.1, 0.08, 1.0), bone(b + s), loc=(xm, y, z + 0.5), bev=0, parent=G))
                quiet(K.box("gal_panel", (bw - 0.1, 0.08, h - 1.7), bone(b), loc=(xm, y, z + 1.7 + (h - 1.7) / 2), bev=0, parent=G))
                pane = void() if rnd.random() < 0.15 else glass()
                quiet(K.box("gal_glass", (bw - 0.1, 0.02, 0.7), pane, loc=(xm, y, z + 1.35), bev=0, parent=G))
            for zz in (z + 1.0, z + 1.7, z + h - 0.08):
                quiet(K.box("gal_belt_rail", (a1 - a0, 0.14, 0.12), steel(), loc=((a0 + a1) / 2, y + s * 0.06, zz), bev=0, parent=G))
        for c0, c1 in cuts:
            xm = (c0 + c1) / 2
            quiet(K.box("gal_door_head", (c1 - c0, 0.08, h - 2.55), bone(1), loc=(xm, y, z + 2.55 + (h - 2.55) / 2), bev=0.01, parent=G))
            for xx in (c0, c1):
                quiet(K.box("gal_door_jamb", (0.14, 0.3, 2.6), graphite(), loc=(xx, y, z + 1.3), bev=0.01, parent=G))
            hazard_band(G, (xm, y + s * 0.05, z + 2.4), c1 - c0 - 0.2, 0.16, yaw=0 if s < 0 else 180, name="gal_door_haz")
        for a0, a1, b0, b1 in holes_cut(x0, x1, z, z + h, [(c0, c1, z, z + 2.55) for c0, c1 in cuts]):
            solid(G, "gal_wall", (a1 - a0, 0.2, b1 - b0), ((a0 + a1) / 2, y, (b0 + b1) / 2))
    for end, xe in zip(ends, (x0, x1)):
        if end:
            quiet(K.box("gal_end", (0.1, w, h), bone(2), loc=(xe, yc, z + h / 2), bev=0.01, parent=G))
            louvre(group(G, "gal_end_louvre", (xe, yc, 0), 90 if xe == x1 else -90), 0.0, z + 1.0, 1.25, 1.25, y=-0.06)
            solid(G, "gal_end", (0.2, w, h), (xe, yc, z + h / 2))
    for x in trestles:
        T = group(G, "trestle")
        quiet(K.box("trestle_footing", (2.2, 2.2, 0.5), concrete(), loc=(x, yc, 0.25), bev=0.03, parent=T))
        _lattice(T, "trestle", (x, yc, 0.5), (x, yc, z - 0.4), 1.6, 0.16, 0.07, steel(), steel())
        quiet(K.box("trestle_head", (0.4, w + 0.8, 0.4), graphite(), loc=(x, yc, z - 0.45), bev=0.02, parent=T))
        solid(T, "trestle", (1.8, 1.8, z - 0.5), (x, yc, z / 2))
    return G


# ---------------------------------------------------------------- the colony's box shed
def bays_for(length, most=5.0):
    """Equal bays of at most most metres along a wall."""
    n = max(1, math.ceil(length / most - 1e-6))
    return [length / n] * n


def shed(P, name, w, d, floors=1, loc=(0, 0, 0), yaw=0.0, plans=None, holes=None, rollers=(), doorways=(), seed=1, gear=True, slab=True):
    """A Colony Works box building w x d on loc (its middle, on the ground): the kit's walls on its outline in bays of
    5 m or less, framed corner posts, a concrete ground slab (its top at 0.3), the flat roof inside the parapet,
    downpipes and, with gear, an air handler and vents on the roof. plans: {side: plan(bay, floor)} for 'front',
    'right', 'back' and 'left'; holes: {side: [(x0, x1, z0, z1)]}; rollers: (side, x, w, h, open_h) roller doors
    with their openings cut; doorways: (side, x) open walk-in doors on the ground floor. Each wall's frame is as in
    warehouses.py (x along it seen from outside). Returns the group, its walls by side and the roof's height."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    hw, hd = w / 2, d / 2
    H = STOREY * floors + SLAB
    plans = plans or {}
    holes = {k: list(v) for k, v in (holes or {}).items()}
    for side, x, dw, dh, _ in rollers:
        holes.setdefault(side, []).append((x - dw / 2, x + dw / 2, 0.0, SLAB + dh))
    for side, x in doorways:
        holes.setdefault(side, []).append(door_hole(x, SLAB))
    frames = {"front": ((0, -hd, 0), 0, w), "right": ((hw, 0, 0), 90, d), "back": ((0, hd, 0), 180, w), "left": ((-hw, 0, 0), -90, d)}
    walls = {}
    for k, (side, (wl, wy, L)) in enumerate(frames.items()):
        walls[side] = wall(G, f"{name}_{side}", L, floors, loc=wl, yaw=wy, bays=bays_for(L), plan=plans.get(side), holes=holes.get(side, ()), seed=seed + k)
    for side, x, dw, dh, oh in rollers:
        roller_door(walls[side], (x, 0, SLAB), dw, dh, oh, lamp=True)
    for side, x in doorways:
        doorway(walls[side], x, SLAB)
    for x, y in ((-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)):
        corner_post(G, (x, y, 0), H + PARAPET)
    if slab:
        K.box("shed_slab", (w, d, SLAB), concrete(), loc=(0, 0, SLAB / 2), bev=0.02, parent=G)
    roof_deck(G, -hw + 0.03, hw - 0.03, -hd + 0.03, hd - 0.03, H)
    for side in ("back", "left"):
        L = frames[side][2]
        downpipe(walls[side], (L / 2 - 1.1, 0, 0), H + PARAPET)
    if gear:
        ac_unit(G, (rnd.uniform(-hw * 0.4, 0), rnd.uniform(0, hd * 0.4), H), yaw=rnd.choice((0, 90)), w=min(3.0, w * 0.3), d=2.0)
        for _ in range(max(1, int(w * d / 60))):
            mushroom_vent(G, (rnd.uniform(-hw + 1.5, hw - 1.5), rnd.uniform(-hd + 1.5, hd - 1.5), H), r=rnd.choice((0.3, 0.35)))
    return G, walls, H


# ---------------------------------------------------------------- the growlab's glass vault
def glass_vault(P, name, L, span, rise, knee=1.25, loc=(0, 0, 0), yaw=0.0, rib=2.5, doors=(1,), gables=(-1, 1), seed=1, broken=0.06):
    """A barrel-vault glasshouse (the growlab's signature volume): a segmental arch of glass on steel-blue arched
    ribs every rib metres, graphite purlins along it, standing on a bone knee wall knee metres high on a concrete
    slab (its top at 0.3), glazed gable ends with an open doorway in the middle third of the end(s) listed in doors
    (+1: the +x end, -1: the -x end; gables lists the ends that have one: an end without butts onto another
    building), a ridge vent, gutters and some panes gone. Runs along x, L long, span wide.
    Collides as its knee walls, its gables round the doorways and the arc in flat strips. Returns the group and the
    arch (knee top, rise, radius, centre height)."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    s = span / 2
    R = (s * s + rise * rise) / (2 * rise)
    zk = SLAB + knee
    zc = zk + rise - R
    th = math.asin(s / R)
    N = 12
    arc = [(R * math.sin(-th + 2 * th * i / N), zc + R * math.cos(-th + 2 * th * i / N)) for i in range(N + 1)]
    K.box("vault_slab", (L, span, SLAB), concrete(), loc=(0, 0, SLAB / 2), bev=0.02, parent=G)
    for sy in (-1, 1):
        quiet(K.box("knee_wall", (L, 0.3, knee), bone(1 + sy), loc=(0, sy * (s - 0.15), SLAB + knee / 2), bev=0.03, parent=G))
        quiet(K.box("knee_cap", (L + 0.1, 0.42, 0.12), graphite(), loc=(0, sy * (s - 0.15), zk + 0.06), bev=0.02, parent=G))
        quiet(K.box("knee_plinth", (L, 0.36, 0.3), concrete(), loc=(0, sy * (s - 0.15), SLAB + 0.15), bev=0.02, parent=G))
        solid(G, "knee_wall", (L, 0.3, knee), (0, sy * (s - 0.15), SLAB + knee / 2))
        quiet(K.box("vault_gutter", (L + 0.2, 0.25, 0.18), steel(), loc=(0, sy * (s + 0.05), zk + 0.1), bev=0.01, parent=G))
    nr = max(2, round(L / rib))
    xs = [-L / 2 + L * k / nr for k in range(nr + 1)]
    for x in xs:
        for (y0, z0), (y1, z1) in zip(arc[:-1], arc[1:]):
            quiet(beam(G, "vault_rib", (x, y0, z0), (x, y1, z1), 0.14, steel(), d=0.24))
        for sy in (-1, 1):
            quiet(K.box("knee_post", (0.2, 0.34, knee + 0.1), steel(), loc=(x, sy * (s - 0.15), SLAB + knee / 2 + 0.05), bev=0.02, parent=G))
    for y, z in arc[1:-1]:
        quiet(K.box("vault_purlin", (L, 0.08, 0.08), graphite(), loc=(0, y, z), bev=0, parent=G))
    glass_m = K.mat("vault_glass", "#9fc6c0", rough=0.05, metal=0.2, ao=0.1)
    for xa, xb in zip(xs[:-1], xs[1:]):
        for (y0, z0), (y1, z1) in zip(arc[:-1], arc[1:]):
            if rnd.random() < broken:
                continue
            face(G, "vault_pane", [(xa + 0.07, y0, z0), (xb - 0.07, y0, z0), (xb - 0.07, y1, z1), (xa + 0.07, y1, z1)], glass_m)
    for (y0, z0), (y1, z1) in zip(arc[:-1], arc[1:]):
        ang = math.degrees(math.atan2(z1 - z0, y1 - y0))
        solid(G, "vault_arc", (L, math.hypot(y1 - y0, z1 - z0), 0.1), (0, (y0 + y1) / 2, (z0 + z1) / 2), rot=(ang, 0, 0))
    quiet(K.box("vault_ridge", (L - 0.6, 0.9, 0.35), zinc(), loc=(0, 0, zk + rise + 0.2), bev=0.02, parent=G))
    for k in range(int(L / 3)):
        quiet(K.box("ridge_vent_flap", (1.6, 0.5, 0.04), glass_m, loc=(-L / 2 + 1.5 + k * 3, 0.55, zk + rise + 0.25), rot=(-25, 0, 0), bev=0, parent=G))
    mul = [-s + span * k / 6 for k in range(7)]

    def height(y):
        return zc + math.sqrt(max(0.0, R * R - y * y))
    for ex in gables:
        x = ex * L / 2
        door = ex in doors
        for y in mul[1:-1]:
            if door and abs(y) < span / 6 + 0.01:
                continue
            quiet(K.box("gable_mullion", (0.12, 0.12, height(y) - zk), steel(), loc=(x, y, (zk + height(y)) / 2), bev=0, parent=G))
        for ya, yb in zip(mul[:-1], mul[1:]):
            mid = (ya + yb) / 2
            opening = door and abs(mid) < span / 6
            zl = SLAB + 2.65 if opening else zk
            if opening:
                quiet(K.box("gable_head", (0.14, yb - ya, 0.16), graphite(), loc=(x, mid, zl - 0.08), bev=0.01, parent=G))
            pts = [(x, ya + 0.06, zl), (x, yb - 0.06, zl), (x, yb - 0.06, height(yb - 0.06) - 0.05), (x, mid, height(mid) - 0.05), (x, ya + 0.06, height(ya + 0.06) - 0.05)]
            if rnd.random() > broken:
                face(G, "gable_pane", pts, glass_m)
            zt = min(height(ya), height(yb))
            z0 = SLAB + 2.65 if opening else SLAB
            solid(G, "gable", (0.16, yb - ya, zt - z0), (x, mid, (z0 + zt) / 2))
            if not opening:
                quiet(K.box("gable_knee", (0.3, yb - ya, knee), bone(2), loc=(x, mid, SLAB + knee / 2), bev=0.02, parent=G))
        if door:
            dw = span / 3
            for sy in (-1, 1):
                quiet(K.box("gable_jamb", (0.2, 0.16, 2.65), graphite(), loc=(x, sy * dw / 2, SLAB + 1.325), bev=0.01, parent=G))
            quiet(K.box("gable_sill", (0.3, dw, 0.03), gunmetal(), loc=(x, 0, SLAB + 0.015), bev=0, parent=G))
            hazard_band(group(G, "gable_haz", (x + ex * 0.1, 0, 0), 90 * ex), (0, 0, SLAB + 2.42), dw - 0.2, 0.16)
            quiet(K.box("gable_lamp", (0.22, 0.5, 0.14), graphite(), loc=(x + ex * 0.2, 0, SLAB + 3.0), bev=0.03, parent=G))
            quiet(K.box("gable_lens", (0.16, 0.42, 0.02), glow(WARM, 10), loc=(x + ex * 0.22, 0, SLAB + 2.925), bev=0, parent=G))
    return G, (zk, rise, R, zc)


# ---------------------------------------------------------------- belts, signs, the yard fence
def belt_conveyor(P, a, b, w=0.8, name="belt", collide=True):
    """A working belt conveyor from a to b (points on its belt's top): steel side rails, a dark belt, end drums, a
    graphite drive box at a, and legs to z = 0 of P every 2 m or less. It collides as one box under it, or with
    collide="legs" (a belt climbing overhead) as its legs only."""
    G = group(P, name)
    a, b = Vector(a), Vector(b)
    d = b - a
    u = d.normalized()
    side = Vector((-u.y, u.x, 0)).normalized()
    quiet(beam(G, "belt_top", a - Vector((0, 0, 0.02)), b - Vector((0, 0, 0.02)), w - 0.1, K.mat("belt", "#232527", rough=0.85, grime=0.3), d=0.04))
    for s in (-1, 1):
        quiet(beam(G, "belt_rail", a + side * s * w / 2 - Vector((0, 0, 0.08)), b + side * s * w / 2 - Vector((0, 0, 0.08)), 0.06, steel(), d=0.22))
    for p in (a, b):
        quiet(rod(G, "belt_drum", p - side * (w / 2) - Vector((0, 0, 0.1)), p + side * (w / 2) - Vector((0, 0, 0.1)), 0.11, graphite(), segs=12))
    quiet(K.box("belt_drive", (0.45, 0.4, 0.4), graphite(), loc=tuple(a - side * (w / 2 + 0.25) - Vector((0, 0, 0.15))), bev=0.02, parent=G))
    n = max(1, math.ceil(d.length / 2.0))
    for k in range(n + 1):
        p = a + d * (k / n) - Vector((0, 0, 0.2))
        for s in (-1, 1):
            quiet(beam(G, "belt_leg", p + side * s * (w / 2 - 0.05), Vector((p.x, p.y, 0)) + side * s * (w / 2 + 0.1), 0.06, steel()))
    if collide == "legs":
        for k in range(n + 1):
            p = a + d * (k / n) - Vector((0, 0, 0.2))
            solid(G, "belt_leg", (0.2, w + 0.3, p.z), (p.x, p.y, p.z / 2), rot=(0, 0, math.degrees(math.atan2(u.y, u.x))))
    elif collide:
        c = (a + b) / 2
        top = max(a.z, b.z)
        solid(G, "belt", (Vector((d.x, d.y, 0)).length, w + 0.2, top), (c.x, c.y, top / 2), rot=(0, 0, math.degrees(math.atan2(u.y, u.x))))
    return G


def pictogram_board(P, loc, kind, yaw=0.0, size=3.0, legs=1.5, name="pictogram"):
    """A framed produce board on legs, facing -y: a bone face in a steel-blue frame with the produce painted on it
    ('vegetable': a leaf-green cabbage head, 'fruit': a signal-red apple with its stalk and leaf), a mustard foot
    rail and two lamps over it. No words, so it reads in any language."""
    G = group(P, name, loc, yaw)
    framed(G, "picto_board", (0, 0, legs + size / 2), (size, 0.2, size), M(BONE, wear=0.3, grime=0.3), steel(), fw=0.14, screws=zinc())
    quiet(K.box("picto_edge", (size + 0.05, 0.22, 0.12), mustard(0.3), loc=(0, 0, legs + 0.06), bev=0.01, parent=G))
    for sx in (-1, 1):
        quiet(K.box("picto_leg", (0.2, 0.2, legs + size * 0.6), steel(), loc=(sx * (size / 2 - 0.3), 0.25, (legs + size * 0.6) / 2), bev=0.02, parent=G))
        quiet(K.box("picto_lamp_arm", (0.06, 0.6, 0.06), graphite(), loc=(sx * size * 0.3, -0.35, legs + size + 0.25), bev=0, parent=G))
        quiet(K.box("picto_lamp", (0.5, 0.22, 0.14), graphite(), loc=(sx * size * 0.3, -0.65, legs + size + 0.25), rot=(-25, 0, 0), bev=0.02, parent=G))
    cz = legs + size / 2
    y = -0.12
    leaf = M("#5f8a4a", wear=0.2, grime=0.2, scratches=0)
    dark = M("#3f6338", wear=0.1, grime=0.1, scratches=0)
    if kind == "vegetable":
        for k, (dx, dz, rr) in enumerate(((0.0, -0.1, 0.95), (-0.55, 0.15, 0.6), (0.55, 0.15, 0.6), (0.0, 0.5, 0.55))):
            quiet(K.cyl("picto_leaf", size * rr / 3, 0.02, leaf if k else M("#7fa85a", wear=0.2, grime=0.2, scratches=0),
                        loc=(dx * size / 3, y + 0.01 * k, cz + dz * size / 3), rot=(90, 0, 0), segs=20, bev=0, parent=G))
        for a in (-50, -20, 0, 20, 50):
            quiet(K.box("picto_vein", (0.05, 0.01, size * 0.28), dark, loc=(math.sin(math.radians(a)) * size * 0.1, y - 0.02, cz - 0.06 * size + math.cos(math.radians(a)) * size * 0.1),
                        rot=(0, a, 0), bev=0, parent=G))
    else:
        red = M(SIGNAL, wear=0.2, grime=0.2, scratches=0)
        for k, dx in enumerate((-0.22, 0.22)):     # two lobes, one a hair in front (no coplanar faces)
            quiet(K.cyl("picto_apple", size * 0.27, 0.02, red, loc=(dx * size / 3, y - 0.006 * k, cz - 0.05 * size), rot=(90, 0, 0), segs=24, bev=0, parent=G))
        quiet(K.box("picto_stalk", (0.08, 0.01, size * 0.16), M(GRAPHITE), loc=(0.02 * size, y - 0.02, cz + 0.27 * size), rot=(0, -12, 0), bev=0, parent=G))
        quiet(K.cyl("picto_leaf", size * 0.09, 0.01, leaf, loc=(0.13 * size, y - 0.02, cz + 0.3 * size), rot=(90, 40, 0), segs=12, bev=0, parent=G))
        quiet(K.cyl("picto_shine", size * 0.05, 0.01, M(BONE), loc=(-0.12 * size, y - 0.03, cz + 0.05 * size), rot=(90, 0, 0), segs=10, bev=0, parent=G))
    solid(G, "picto", (size, 0.3, size), (0, 0, legs + size / 2))
    return G


def fence_ring(P, pts, gates=None, h=2.25, name="yard_fence", lite=True):
    """A welded-mesh fence round a closed outline of ground points (x, y), its corners in order; gates: {side index:
    (t0, t1)} leaves that stretch of the side open, its sliding gate pushed back beside it."""
    G = group(P, name)
    gates = gates or {}
    for k in range(len(pts)):
        mesh_fence(G, pts[k], pts[(k + 1) % len(pts)], h=h, gate=gates.get(k), name=f"{name}_{k}", lite=lite)
    return G


def lamppost(P, loc, h=4.5, yaw=0.0, lit=True, board=None, name="lamppost"):
    """A square Colony Works lamppost: a steel-blue square post on a framed concrete foot with a hazard band, a
    graphite arm out towards -y and a square head with a warm lens (lit or long dead). board: a short code painted on
    a mustard plate strapped to the post."""
    G = group(P, name, loc, yaw)
    quiet(K.box("lp_foot", (0.6, 0.6, 0.35), concrete(), loc=(0, 0, 0.175), bev=0.03, parent=G))
    quiet(K.box("lp_base", (0.36, 0.36, 0.9), steel(), loc=(0, 0, 0.8), bev=0.03, parent=G))
    quiet(K.box("lp_post", (0.2, 0.2, h - 0.35), steel(), loc=(0, 0, 0.35 + (h - 0.35) / 2), bev=0.02, parent=G))
    hazard_band(G, (0, -0.19, 0.45), 0.36, 0.6, name="lp_haz")
    quiet(K.box("lp_arm", (0.12, 1.2, 0.12), graphite(), loc=(0, -0.55, h - 0.1), bev=0.01, parent=G))
    quiet(beam(G, "lp_brace", (0, -0.05, h - 0.75), (0, -0.7, h - 0.15), 0.06, graphite()))
    quiet(K.box("lp_head", (0.5, 0.55, 0.22), graphite(), loc=(0, -1.1, h - 0.12), bev=0.03, parent=G))
    quiet(K.box("lp_lens", (0.42, 0.46, 0.02), glow(WARM, 10) if lit else glass(), loc=(0, -1.1, h - 0.235), bev=0, parent=G))
    if lit:
        K.point("lp_light", (0, -1.1, h - 0.6), WARM, 120, 0.2).parent = G
    if board:
        quiet(K.box("lp_board", (0.5, 0.04, 0.7), mustard(0.3), loc=(0, -0.13, 2.2), bev=0.01, parent=G))
        painted(G, board, (0, -0.16, 2.2), 0.16, graphite())
    solid(G, "lp_post", (0.36, 0.36, h), (0, 0, h / 2))
    return G
