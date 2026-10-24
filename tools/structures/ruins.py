"""The Old Stone ruins (docs/art-direction.md), assembled from stone_kit.py: the POIs that were placeholder boxes.

Each keeps its placeholder prefab's footprint and markers (tools/author-poi-placeholders.py): every crate, chest and
sign stands where it stood, on stone or ground at the height it had. Blender frame: origin on the ground at the
footprint's centre, front towards -Y (the POI's +Z), so a marker at POI (x, y, z) is at (x, -z, y) here.

* ``small_ruin`` (14 x 12 m) - a small chapel on a flagstone terrace: an arched door, a wall broken down to a stub,
  a porch of two pillars, and a stair up the side to the stump of its bell tower.
* ``ruin`` (30 x 24 m) - a great hall: the back wall still 10 m high with two rows of arched windows, the front
  fallen, a raised dais at the back with steps up, and from it a stair along the back wall to a gallery under the
  upper windows.
* ``forest_ruin`` (16 x 16 m) - the corner of a temple the forest has taken back: two walls, a column, an altar
  platform with probe crystals growing from the altar, flagstones half under the moss, a tree in the nave.
* ``mountain_lookout`` (10 x 10 m) - a square watchtower 16 m high: a stair winding up inside to the top floor at
  14.75 m behind a broken crenellated parapet, where the crate is, and crystals glowing on the parapet.
* ``ruin_city`` (160 x 140 m) - a ruined stone town (ruin_city.py), split into five models.
"""
import math
import random

import bpy

import kit as K
import stone_kit as S
from landmarks import tree, bush


def _root(name):
    return K.link(bpy.data.objects.new(name, None))


def terrace(site, x0, x1, y0, y1, z=0.3, keep=(), gone=0.1, edge=True):
    """A flagstone terrace with its top at z (the placeholder's pad): flags over a bed, a kerb of bevelled blocks round
    its edge, grass at its foot."""
    S.floor(site, x0, x1, y0, y1, z, keep=keep, gone=gone, lifted=0.04)
    if not edge:
        return
    rnd = site.rnd
    for (a, b) in (((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))):
        ln = S.Line(a, b)
        u = 0.0
        while u < ln.L - 0.1:
            L = min(rnd.uniform(0.9, 1.6), ln.L - u)
            if rnd.random() > 0.08:
                site.cut.box((L - 0.03, 0.45, z + 0.02), ln.at(u + L / 2, 0.2, (z + 0.02) / 2 - 0.01), (0, 0, ln.yaw + rnd.uniform(-1, 1)), site.stone_or_dark(0.12))
            u += L
        S.grass_along(site, ln, 0, ln.L, -0.4, 0.0, every=1.3)
    for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
        site.cut.box((0.6, 0.6, z + 0.04), (x + (0.2 if x == x0 else -0.2), y + (0.2 if y == y0 else -0.2), (z + 0.04) / 2 - 0.01), (0, 0, 0), site.s)


# ================================================================== small ruin
def small_ruin(seed=11):
    """A chapel 10 x 8 m on a terrace 21 x 18 m (top 0.3 m), its door to the front; the crate stands on the terrace at
    its west end (POI -9, 0.3, 2)."""
    site = S.Site("small_ruin", seed)
    rnd = site.rnd
    crate = (-9.0, -2.0, 0.6)
    terrace(site, -11.0, 10.0, -9.0, 9.0, keep=[crate])
    # the chapel: front door arched, the east wall broken down, a window each side
    S.house(site, -5.0, -2.0, 5.0, 6.0, z0=0.3, storeys=1, t=0.75, doors=(("s", 0.5),), ruin="half", low={"e"}, arch_doors=True, seed=seed)
    # the bell tower's stump at the north-west corner: a solid block of courses, its top 3.25 m up, a stair up the
    # chapel's west side to it
    S.wall(site, (-6.5, 3.0), (-6.5, 7.0), S.broken(4.0, 3.3, 3.3, rnd), t=2.5, z0=0.0)
    S.stair(site, (-5.75 - 0.6, -1.75, 0.3), (-5.75 - 0.6, 3.0, 3.25), 1.2)
    S.rail(site, (-7.1, -0.5, 0.3 + 0.75), (-7.1, 3.0, 3.25), h=0.6, t=0.3)
    S.moss(site, (-6.0, 6.2, 3.27), 0.35)
    # a porch of two pillars before the door, one fallen
    S.pillar(site, (-2.2, -3.6, 0.3), 3.2, w=0.7)
    S.pillar(site, (2.2, -3.6, 0.3), 3.2, w=0.7, broken_at=1.4)
    # what fell from the east wall, inside and out
    S.rubble(site, (5.8, 2.0, 0.3), 1.8, 2.6, 0.9)
    S.rubble(site, (3.0, 3.8, 0.3), 1.2, 1.4, 0.5)
    S.fallen(site, (7.5, -1.0, 0.3), (1.0, 0.7, 0.5), lean=12, yaw=25)
    S.fallen(site, (6.8, -4.5, 0.3), (0.8, 0.6, 0.5), lean=70, yaw=-30)
    # moss and grass round it, a bush at the back
    bush(site.P, "bush", (8.5, 7.0, 0.3), 1.2, rnd)
    bush(site.P, "bush", (-10.0, 7.5, 0.3), 1.0, rnd, shade=2)
    for k in range(20):
        S.grass(site, (rnd.uniform(-10.5, 9.5), rnd.uniform(-8.5, 8.5), 0.3))
    return site.done()


# ================================================================== ruin
def ruin(seed=12):
    """A great hall 30 x 24 m on a terrace 34 x 28 m (top 0.3 m). Crate at POI (-10, 0.3, 4), skull sign at POI
    (-14.5, 0.3, 8) against the inside of the west wall."""
    site = S.Site("ruin", seed)
    rnd = site.rnd
    crate, sign = (-10.0, -4.0, 0.6), (-14.5, -8.0, 0.4)
    terrace(site, -17.0, 17.0, -14.0, 14.0, keep=[crate, sign])
    z0, t = 0.3, 0.8
    X0, X1, Y0, Y1 = -15.6, 15.0, -12.0, 12.4
    # the back wall: two rows of arched windows, standing 10 m
    back = S.broken(X1 - X0, 10.3, 8.4, rnd, points=5)
    holes = [S.window(u, z0, w=1.5, h=2.75, sill=1.2, arch=True) for u in (21.5, 26.5)]
    holes += [S.window(u, z0, w=1.5, h=2.25, sill=7.1, arch=True) for u in (3.0, 7.0, 11.0, 19.0, 23.0, 27.0)]
    ln = S.wall(site, (X1, Y1 - t / 2), (X0, Y1 - t / 2), back, t=t, z0=0.0, holes=holes, ch=0.5)
    S.sills(site, ln, holes, t, back)
    # the west wall: 7 m, arched windows
    west = S.broken(Y1 - Y0 - 2 * t, 7.3, 5.2, rnd, points=4)
    holes = [S.window(u, z0, w=1.25, h=2.5, sill=1.4, arch=True) for u in (3.5, 9.5, 15.5)]
    ln = S.wall(site, (X0 + t / 2, Y1 - t), (X0 + t / 2, Y0 + t), west, t=t, z0=0.0, holes=holes, bond=1)
    S.sills(site, ln, holes, t, west)
    # the east wall: broken down, a breach
    east = S.broken(Y1 - Y0 - 2 * t, 5.0, 1.0, rnd, points=5, breach=0.3)
    S.wall(site, (X1 - t / 2, Y0 + t), (X1 - t / 2, Y1 - t), east, t=t, z0=0.0, holes=[S.window(5.0, z0, sill=1.2)], bond=1)
    # the front: the right end still 4 m with its doorway, the rest fallen to stubs
    S.wall(site, (X0, Y0 + t / 2), (-1.0, Y0 + t / 2), S.collapsed(14.6, rnd, hi=1.3), t=t, z0=0.0)
    front = lambda u: 4.3 - max(0.0, 3.0 - u) * 0.9
    S.wall(site, (3.0, Y0 + t / 2), (X1, Y0 + t / 2), front, t=t, z0=0.0, holes=[S.door(6.0, z0, w=2.0, h=2.75, arch=True)])
    S.rubble(site, (1.0, Y0 + 0.2, 0.3), 2.6, 1.6, 0.8, walk=True)
    S.rubble(site, (-9.0, Y0 - 1.2, 0.3), 2.0, 1.4, 0.6)
    # the dais at the back right: 3 m of courses with a flagged top, steps up its front, a stair along the back wall
    # from its top to the gallery under the upper windows
    dx0, dx1, dy0, dy1, dz = -1.0, X1 - t, -2.0, Y1 - t, 3.3
    S.wall(site, (dx0, dy0 + 0.4), (dx1, dy0 + 0.4), 3.25, t=0.8, z0=0.0)
    S.wall(site, (dx0 + 0.4, dy1), (dx0 + 0.4, dy0 + 0.8), 3.25, t=0.8, z0=0.0, bond=1)
    S.wall(site, (dx1 - 0.4, dy0 + 0.8), (dx1 - 0.4, dy1), 3.25, t=0.8, z0=0.0, bond=1, grass=False)
    site.blocks.box((dx1 - dx0, dy1 - dy0, 0.4), ((dx0 + dx1) / 2, (dy0 + dy1) / 2, dz - 0.2 - 0.01), (0, 0, 0), site.mo)
    site.col.box((dx1 - dx0, dy1 - dy0, dz), ((dx0 + dx1) / 2, (dy0 + dy1) / 2, dz / 2))
    S._flag_top(site, dx0, dx1, dy0, dy1, dz)
    S.stair(site, (6.5, dy0 - 4.5, z0), (6.5, dy0 + 0.02, dz), 5.0)
    gz = 6.8
    S.stair(site, (4.0, dy1 - 0.85, dz), (-0.5, dy1 - 0.85, gz), 1.5, base=3.5)
    # the gallery: a ledge of big slabs along the back wall, its front edge broken
    S.slab(site, X0 + t, -0.5, dy1 - 2.25, dy1, gz, t=0.45, ragged=("y0", 0.5), below=z0)
    S.rail(site, (-0.65, dy1 - 2.2, gz), (-0.65, dy1 - 1.65, gz), h=0.9, t=0.3)
    for x in (-12.0, -7.0, -2.5):
        site.cut.box((0.6, 0.9, 0.45), (x, dy1 - 0.45, gz - 0.65), (0, 0, 0), site.sd)       # corbels under it
    # pillars down the hall, two standing, one broken, one fallen; a column on the dais
    S.pillar(site, (-6.0, 6.0, z0), 5.5, w=0.9)
    S.pillar(site, (-6.0, 0.0, z0), 5.5, w=0.9, broken_at=2.6)
    S.pillar(site, (-6.0, -6.0, z0), 5.5, w=0.9)
    S.column(site, (10.5, 6.5, dz), 4.0, r=0.4, broken_at=2.0)
    S.fallen(site, (-11.0, 6.0, z0), (2.4, 0.8, 0.8), lean=8, yaw=70)
    S.rubble(site, (13.0, -6.0, 0.3), 1.6, 2.4, 1.0)
    S.crystals(site, (11.0, 9.5, dz), n=6, h=1.6, energy=900)
    bush(site.P, "bush", (16.0, 13.0, 0.3), 1.4, rnd)
    bush(site.P, "bush", (-16.0, -13.0, 0.3), 1.0, rnd, shade=2)
    tree(site.P, "tree", (-12.5, 9.5, 0.3), 9.0, rnd, crown=2.2)
    for k in range(30):
        S.grass(site, (rnd.uniform(-16.5, 16.5), rnd.uniform(-13.5, 13.5), 0.3))
    return site.done()


# ================================================================== forest ruin
def forest_ruin(seed=13):
    """The corner of a temple in the forest, 16 x 16 m on the ground (its markers stand at ground level: crates at POI
    (-5, 0, -5) and (4, 0, 5), the Ruin Chest against the inside of the west wall at (-6.4, 0, 0))."""
    site = S.Site("forest_ruin", seed)
    rnd = site.rnd
    marks = [(-5.0, 5.0, 0.6), (4.0, -5.0, 0.6), (-6.4, 0.0, 0.7)]
    t = 0.7
    # the west wall, its inner face 7 m from the centre, an arched window high up
    west = S.broken(10.85, 4.6, 3.2, rnd, points=4)
    holes = [S.window(7.0, 0.0, w=1.0, h=1.75, sill=1.75, arch=True)]
    ln = S.wall(site, (-7.0 - t / 2, 7.85), (-7.0 - t / 2, -3.0), west, t=t, z0=-0.3, holes=holes, bond=1)
    S.sills(site, ln, holes, t, west)
    # the north wall: 4 m at the corner sloping to 2 m, a gap, a fragment
    S.wall(site, (-7.0 - t, 7.5), (0.0, 7.5), lambda u: 4.1 - max(0.0, u - 3.0) * 0.5, t=t, z0=-0.3)
    S.wall(site, (3.5, 7.5), (6.5, 7.5), S.broken(3.0, 2.0, 1.0, rnd, points=3), t=t, z0=-0.3)
    # the east wall, low
    S.wall(site, (7.5, -5.5), (7.5, -0.5), S.broken(5.0, 1.5, 0.9, rnd, points=3), t=t, z0=-0.3, bond=1)
    # the column at the south-west corner, whole, 5 m
    S.column(site, (-7.5, -7.5, 0.0), 5.0, r=0.5)
    # the altar platform in the north-east: steps up its front, an altar the crystals have split
    px0, px1, py0, py1, pz = 1.0, 7.5, 3.75, 7.5 - t / 2, 1.3
    S.wall(site, (px0, py0 + 0.35), (px1, py0 + 0.35), 1.25, t=0.7, z0=-0.3)
    S.wall(site, (px0 + 0.35, py1), (px0 + 0.35, py0 + 0.7), 1.25, t=0.7, z0=-0.3, bond=1)
    S.wall(site, (px1 - 0.35, py0 + 0.7), (px1 - 0.35, py1), 1.25, t=0.7, z0=-0.3, bond=1)
    site.blocks.box((px1 - px0, py1 - py0, 0.35), ((px0 + px1) / 2, (py0 + py1) / 2, pz - 0.175 - 0.01), (0, 0, 0), site.mo)
    site.col.box((px1 - px0, py1 - py0, pz), ((px0 + px1) / 2, (py0 + py1) / 2, pz / 2))
    S._flag_top(site, px0, px1, py0, py1, pz)
    S.stair(site, (4.75, py0 - 1.75, 0.0), (4.75, py0 + 0.02, pz), 4.0)
    site.cut.box((1.8, 1.0, 1.0), (4.5, 6.2, pz + 0.5), (0, 0, 4), site.sd)
    site.col.box((1.8, 1.0, 1.0), (4.5, 6.2, pz + 0.5), (0, 0, 4))
    S.crystals(site, (4.5, 6.2, pz + 1.0), n=6, h=1.5, energy=700)
    # flagstones half under the moss: none where the crates and the chest stand
    S.floor(site, -6.9, 3.5, -6.5, 7.1, 0.03, gone=0.25, lifted=0.06, keep=marks, bed=0.0, collide=False, holes=marks, thick=0.1)
    S.fallen(site, (2.0, -2.0, 0.0), (2.0, 1.5, 0.8), lean=6, yaw=30)
    S.fallen(site, (-3.0, -5.5, 0.0), (1.2, 0.7, 0.5), lean=60, yaw=-20)
    S.rubble(site, (7.6, 1.6, 0.0), 1.4, 1.8, 0.7)
    S.rubble(site, (1.6, 8.6, 0.0), 1.6, 1.0, 0.6)
    S.pillar(site, (-2.0, -7.0, 0.0), 3.0, w=0.8, broken_at=1.3)
    S.column(site, (-3.5, 1.5, 0.0), 4.5, r=0.45, broken_at=1.2)
    for k, (x, y, r) in enumerate(((-8.6, 8.6, 1.0), (8.4, 7.8, 0.9), (-8.6, -3.8, 0.8))):
        bush(site.P, f"bush{k}", (x, y, 0.0), r, rnd, shade=k)
    for k in range(28):
        S.grass(site, (rnd.uniform(-7.5, 7.5), rnd.uniform(-7.5, 7.5), 0.0))
    return site.done()


# ================================================================== mountain lookout
def mountain_lookout(seed=14):
    """A watchtower 10 m square on a plinth: its top floor at 14.75 m holds the crate (POI 2.5, 14.75, 2.5)."""
    site = S.Site("mountain_lookout", seed)
    rnd = site.rnd
    # the plinth it stands on, a step all round
    site.cut.box((12.0, 12.0, 0.3), (0, 0, 0.14), (0, 0, 0), site.s)
    site.col.box((12.0, 12.0, 0.3), (0, 0, 0.15))
    S._flag_top(site, -6.0, 6.0, -6.0, 6.0, 0.3, size=1.5)
    top, hole = S.tower(site, 0.0, 0.0, 10.0, 0.3, 14.75, t=0.9, width=1.5, start=0, door_side="s",
                        broken_sides=("n",), spike=("w", 20.5, 0.85))
    # crystals grown out of the parapet's broken north-west corner, a fallen merlon on the floor
    S.crystals(site, (-3.4, 3.4, 14.75), n=7, h=1.8, energy=1200)
    S.fallen(site, (2.0, 2.6, 14.75), (1.0, 0.9, 0.75), lean=70, yaw=20, collide=False)
    S.moss(site, (3.4, 3.2, 14.77), 0.4)
    # the rubble of the north parapet at the tower's foot, and a broken outwork wall
    S.rubble(site, (2.0, 7.4, 0.3), 2.4, 1.4, 0.8)
    S.wall(site, (-6.6, -4.0), (-6.6, 3.0), S.broken(7.0, 1.8, 0.6, rnd, points=4), t=0.7, z0=-0.3, bond=1)
    # a lean-to's shell against the east side, its roof long gone
    S.house(site, 5.0, -3.5, 10.5, 3.5, z0=0.3, storeys=1, t=0.6, doors=(("s", 0.5),), ruin="shell", windows=False, seed=seed + 1)
    for k in range(14):
        a = rnd.uniform(0, 6.28)
        S.grass(site, (math.cos(a) * rnd.uniform(5.2, 6.0), math.sin(a) * rnd.uniform(5.2, 6.0), 0.3))
    return site.done()


RUINS = {
    "small_ruin": small_ruin,
    "ruin": ruin,
    "forest_ruin": forest_ruin,
    "mountain_lookout": mountain_lookout,
}
