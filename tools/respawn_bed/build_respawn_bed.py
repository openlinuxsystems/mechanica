"""Builds the Respawn Bed: a rectangular colony sleeping bag lying on a foam ground mat (4 x 1 x 8 blocks).

    python3 tools/respawn_bed/build_respawn_bed.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/respawn_bed.glb`` (no clips) and ``mods/vanilla/textures/items/respawn_bed_icon.png``;
with ``--render`` the three-quarter, front, side and top review views and the sheet. Blender's Python module (bpy)
runs it; nothing is downloaded. The build is deterministic.

Colony Works (``docs/art-direction.md``), soft goods: a steel-blue quilted bag (horizontal baffles), rectangular, not
mummy-shaped, its top flap folded back at the head end to show the bone lining quilted in steel blue, a graphite zip
with steel teeth down its right side (open at the head, the slider and its mustard pull where the flap turns back), a
rolled-up stuff sack at the head with a mustard drawcord and graphite straps, a bone label on the foot, all on a
ridged graphite foam mat whose edge shows round the bag. No frame, legs or headboard: the Fancy Bed is the real bed.
No cyan (it is unpowered), never orange. Axes as ``station_kit``: x right, z up, the foot (front) along -y.
"""

import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import station_kit as S  # noqa: E402
import working_kit as W  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from part_kit import Geo, box, cylinder  # noqa: E402

lt = K.lt

FLOOR = -0.125                  # the part is 1 block tall: 0.25 m, centred
HALF = (0.5, 1.0, 0.125)
MAT_T = 0.016                   # foam mat thickness
BAG_HW = 0.415                  # the bag's half width (rectangular: the same from head to foot)
FOOT, HEAD = -0.925, 0.925      # the bag's ends
FOLD = 0.34                     # where the top flap turns back (head side of it is open)
FLAP = 0.27                     # how far the folded flap lies back towards the foot
BAFFLE = 0.21                   # quilting baffle pitch
PUFF, SEAM = 0.074, 0.058       # the closed bag's top: mid-baffle and at a seam (the collision box top is 0.085)
ZIP_Z = -0.026                  # the zip runs along the side at about the bag's widest


def mat_surface(seed, color):
    """Closed-cell foam: the fabric's mottling with a shallow groove every sixteenth of the tile (ribs along the mat)."""
    import numpy as np
    albedo, height, rough, metal = K.fabric_surface(seed, color)
    size = albedo.shape[0]
    xx = np.mgrid[0:size, 0:size][1]
    phase = (xx % (size // 16)) / (size // 16)
    groove = np.clip(1 - np.abs(phase - 0.5) * 7, 0, 1)
    albedo = albedo * (1 - 0.35 * groove[..., None])
    return albedo, height * 0.3 - 1.2 * groove, np.full_like(rough, 0.9), metal


def bag_materials(m):
    tex = lt.textured
    m.update({
        "foam": tex("s_foam", K.half_res(mat_surface(631, "#2b2f35")), normal_strength=2.0),
    })


def baffle_keys(y0, y1, puff, seam, hw, zb, end_round=True):
    """Key sections (y, xc, hw, zb, zt) of a quilted run from y0 (foot) to y1: puffed mid-baffle, pinched at seams."""
    n = max(1, round((y1 - y0) / BAFFLE))
    pitch = (y1 - y0) / n
    keys = []
    if end_round:
        keys += [(y0 - 0.012, 0, hw - 0.03, zb + 0.012, seam - 0.03), (y0, 0, hw - 0.008, zb + 0.002, seam - 0.006)]
    else:
        keys += [(y0, 0, hw, zb, seam)]
    for k in range(n):
        s0, s1 = y0 + k * pitch, y0 + (k + 1) * pitch
        # a flat-topped puff between narrow stitched seams (a quilted bag, not an air mattress)
        keys.append((s0 + 0.04, 0, hw + 0.002, zb, puff - 0.006))
        keys.append(((s0 + s1) / 2, 0, hw + 0.004, zb, puff))
        keys.append((s1 - 0.04, 0, hw + 0.002, zb, puff - 0.006))
        keys.append((s1, 0, hw - 0.002, zb, seam))
    return keys


def soft_roll(g, a, b, r, mat, squash=1.0, pinch=0.85, ring_pts=24, power=2.4):
    """A soft fabric roll from point a to b (a stuff sack, a fold): a lofted round section that eases in at the ends."""
    a, b = Vector(a), Vector(b)
    length = (b - a).length
    keys = [(0, 0, r * pinch * 0.7, -r * squash * pinch * 0.7, r * squash * pinch * 0.7),
            (length * 0.06, 0, r * 0.97, -r * squash * 0.97, r * squash * 0.97),
            (length * 0.5, 0, r, -r * squash, r * squash),
            (length * 0.94, 0, r * 0.97, -r * squash * 0.97, r * squash * 0.97),
            (length, 0, r * pinch * 0.7, -r * squash * pinch * 0.7, r * squash * pinch * 0.7)]
    sections = K.smooth_sections(keys, steps=3)
    d = (b - a).normalized()
    rot = Vector((0, 1, 0)).rotation_difference(d).to_matrix().to_4x4()
    K.squircle_loft(g, sections, mat, ring_pts=ring_pts, power=power, matrix=Matrix.Translation(a) @ rot)


def build_sleeping_bag(m):
    bag_materials(m)
    root = K.make_empty("RespawnBed")
    g = Geo()
    zb = FLOOR + MAT_T                      # the bag lies on the mat

    # ground mat, a little bigger than the bag so its ribbed edge shows all round
    box(g, (0, 0, FLOOR + MAT_T / 2), (BAG_HW + 0.05, HEAD + 0.045, MAT_T / 2), "foam", bevel=0.006)

    # the closed bag, foot to the fold: two layers of quilted steel-blue wool, horizontal baffles
    closed = K.smooth_sections(baffle_keys(FOOT, FOLD, PUFF, SEAM, BAG_HW, zb), steps=4)
    K.squircle_loft(g, closed, "wool", ring_pts=32, power=3.4)

    # open at the head: the bag's lower layer, its bone lining (quilted in steel blue) facing up
    low_top = zb + 0.05
    low_mid = (zb + low_top) / 2
    lower = K.smooth_sections([(FOLD - 0.06, 0, BAG_HW, zb, low_top + 0.006), (FOLD + 0.1, 0, BAG_HW, zb, low_top),
                               (HEAD - 0.06, 0, BAG_HW, zb, low_top), (HEAD, 0, BAG_HW - 0.01, zb + 0.002, low_top - 0.004),
                               (HEAD + 0.012, 0, BAG_HW - 0.035, zb + 0.012, low_top - 0.02)], steps=3)
    K.squircle_loft(g, lower, "wool", ring_pts=32, power=3.4)
    box(g, (0, (FOLD + HEAD - 0.03) / 2, low_top - 0.002), (BAG_HW - 0.03, (HEAD - 0.03 - FOLD) / 2, 0.006), "quilt", bevel=0.005)

    # the top flap, turned back over the bag at the fold: a soft roll at the fold, then the flap lining-up towards the foot
    roll_r = 0.037
    roll_z = PUFF - 0.004
    soft_roll(g, (-BAG_HW + 0.004, FOLD + 0.006, roll_z), (BAG_HW - 0.004, FOLD + 0.006, roll_z), roll_r, "wool", squash=1.0, pinch=0.92,
              ring_pts=28)
    flap_zb, flap_zt = PUFF - 0.016, PUFF + 0.028
    e = FOLD - FLAP
    flap = K.smooth_sections([(e - 0.012, 0, BAG_HW - 0.02, flap_zb + 0.008, flap_zt - 0.02),
                              (e, 0, BAG_HW - 0.006, flap_zb + 0.002, flap_zt - 0.008),
                              (e + 0.035, 0, BAG_HW + 0.002, flap_zb, flap_zt - 0.002),
                              (e + FLAP * 0.32, 0, BAG_HW + 0.003, flap_zb, flap_zt),
                              (e + FLAP * 0.5, 0, BAG_HW, flap_zb + 0.004, flap_zt - 0.011),
                              (e + FLAP * 0.68, 0, BAG_HW + 0.003, flap_zb + 0.002, flap_zt),
                              (FOLD - 0.03, 0, BAG_HW, roll_z - 0.01, roll_z + roll_r - 0.002),
                              (FOLD, 0, BAG_HW - 0.004, roll_z - 0.02, roll_z + roll_r - 0.006)], steps=4)
    K.squircle_loft(g, flap, "quilt", ring_pts=32, power=3.0)
    # the flap's hem: a mustard binding along its free edge
    soft_roll(g, (-BAG_HW + 0.016, e - 0.008, flap_zb + 0.02), (BAG_HW - 0.016, e - 0.008, flap_zb + 0.02),
              0.012, "mustard", pinch=0.95, ring_pts=12, power=2.0)

    # graphite piping down the left side and across the foot (the bag's seam), and down the open lower layer
    pz = ZIP_Z
    left = [(-BAG_HW - 0.002, y, pz) for y in (FOLD - 0.02, 0.0, FOOT + 0.06)]
    foot = [(-BAG_HW + 0.03, FOOT - 0.009, pz), (0, FOOT - 0.011, pz), (BAG_HW - 0.03, FOOT - 0.009, pz)]
    K.bl.sweep(g, [*left, (-BAG_HW + 0.002, FOOT + 0.004, pz), *foot, (BAG_HW + 0.0, FOOT + 0.006, pz), (BAG_HW + 0.003, FOOT + 0.06, pz)],
               0.005, "fabric_dark", segs=8)
    K.bl.sweep(g, [(-BAG_HW - 0.001, y, low_mid) for y in (FOLD - 0.02, (FOLD + HEAD) / 2, HEAD - 0.05)], 0.005, "fabric_dark", segs=8)

    # the zip down the right side: graphite tape and steel teeth up to the slider at the fold; past it the zip is
    # open, its lower half running on along the lower layer
    x = BAG_HW + 0.001

    def zip_run(y0, y1, z, half_teeth=False):
        box(g, (x, (y0 + y1) / 2, z), (0.004, (y1 - y0) / 2, 0.009), "fabric_dark", bevel=0.002)
        n = int((y1 - y0) / 0.013)
        for k in range(n):
            y = y0 + 0.006 + k * (y1 - y0 - 0.012) / (n - 1)
            box(g, (x + 0.004, y, z + (0.002 if half_teeth else 0)), (0.0016, 0.0035, 0.003 if half_teeth else 0.0045), "steel")

    y0, y1 = FOOT + 0.06, FOLD - 0.03
    zip_run(y0, y1, ZIP_Z)
    zip_run(FOLD + 0.05, HEAD - 0.05, low_mid, half_teeth=True)
    # slider, pull and its mustard cord loop
    sy = y1 + 0.008
    box(g, (x + 0.008, sy, ZIP_Z), (0.005, 0.014, 0.008), "steel", bevel=0.002)
    box(g, (x + 0.014, sy - 0.026, ZIP_Z - 0.006), (0.0025, 0.016, 0.006), "graphite", bevel=0.0015,
        rot=Matrix.Rotation(math.radians(-20), 3, "X"))
    K.bl.sweep(g, [(x + 0.016, sy - 0.04, ZIP_Z - 0.012), (x + 0.02, sy - 0.06, ZIP_Z - 0.03), (x + 0.018, sy - 0.075, ZIP_Z - 0.045),
                   (x + 0.012, sy - 0.07, ZIP_Z - 0.05), (x + 0.012, sy - 0.05, ZIP_Z - 0.03), (x + 0.014, sy - 0.04, ZIP_Z - 0.014)],
               0.0035, "mustard", segs=6)

    # a bone label stitched on the foot (the colony's bag tag, no text) with a mustard band
    box(g, (0.18, FOOT - 0.006, -0.002), (0.06, 0.003, 0.026), "bone", bevel=0.0015)
    box(g, (0.18, FOOT - 0.0085, 0.014), (0.06, 0.0012, 0.005), "mustard", bevel=0.0008)

    # the rolled-up stuff sack at the head: dark steel-blue, two graphite straps, a mustard drawcord and toggle
    sr = 0.062
    sz = low_top + sr * 0.92
    sy0 = HEAD - 0.2
    a, b = (-0.25, sy0, sz), (0.25, sy0, sz)
    soft_roll(g, a, b, sr, "wool_dark", squash=0.92, pinch=0.8, ring_pts=28, power=2.2)
    for sx in (-0.13, 0.13):
        cylinder(g, (sx - 0.012, sy0, sz), (sx + 0.012, sy0, sz), sr * 1.0 + 0.003, "fabric_dark", segs=24, r1=sr * 1.0 + 0.003)
        box(g, (sx, sy0 - 0.004, sz + sr * 0.92 + 0.004), (0.014, 0.012, 0.004), "steel", bevel=0.0015)
    cylinder(g, (0.25, sy0, sz), (0.268, sy0, sz), 0.02, "wool_dark", segs=14, r1=0.008)
    K.bl.sweep(g, [(0.266, sy0, sz), (0.285, sy0 - 0.01, sz - 0.02), (0.29, sy0 - 0.03, sz - 0.045), (0.295, sy0 - 0.06, low_top + 0.006)],
               0.0035, "mustard", segs=6)
    cylinder(g, (0.287, sy0 - 0.028, sz - 0.04), (0.293, sy0 - 0.042, sz - 0.052), 0.008, "graphite", segs=10)

    K.make_object("Body", g, m, root)
    rig = {"root": root, "clips": {}}
    rig["rest"] = lambda: None
    rig["states"] = [("LAID OUT", lambda: None)]
    return rig


def review(name, title, lines, rig, icon, info, out, half):
    """station_kit.review's views plus one from straight above (head at the top)."""
    import bpy
    import render_kit as rk
    rl = rk.rl
    rk.VIEWS = ("three_quarter", "front", "side", "top")
    rk.TITLES = {**rk.TITLES, "top": "TOP (head up)"}
    out.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    root = rig["root"]
    ground = rl.setup_world()
    sc.cycles.samples = rk.SAMPLES
    W.reset(rig)
    rig["rest"]()
    lo, _hi = rk._bounds(root)
    size = max(half) * 2
    ground.location.z = lo.z - 0.0005
    target = Vector((0, 0, lo.z + half[2] * 0.95))
    cams = {
        "three_quarter": rl.camera("Q", target + Vector((1.0, -1.25, 0.75)).normalized() * size * 1.9, target, lens=50),
        "front": rl.camera("F", target + Vector((0, -20, 0)), target, ortho=1.25),
        "side": rl.camera("S", target + Vector((20, 0, 0)), target, ortho=size * 1.15),
        "top": rl.camera("T", target + Vector((0, 0, 20)), target, ortho=size * 1.2),
    }
    paths = {v: [] for v in rk.VIEWS}
    for label, pose in rig["states"]:
        W.reset(rig)
        pose()
        for view in rk.VIEWS:
            p = out / "frames" / f"{name}_{label.lower().replace(' ', '_')}_{view}.png"
            p.parent.mkdir(parents=True, exist_ok=True)
            rl.render(cams[view], p, rk.W, rk.H)
            paths[view].append((label, p))
    for cam in cams.values():
        bpy.data.objects.remove(cam)
    rk.sheet(name, title, lines, paths, [(rig["states"][-1][0], icon)], [rk.fmt_stats(None, info)], out)


def main():
    part = "respawn_bed"
    K.reset_scene()
    m = S.materials()
    rig = build_sleeping_bag(m)
    root = rig["root"]
    info = K.export(root, K.MESHES / f"{part}.glb", animations=False)
    K.report(K.MESHES / f"{part}.glb", info)
    K.strip_animation(root)
    icon = K.icon(root, K.ICONS / f"{part}_icon.png", S.FRONT_ICON, 1.9, (0, 0.04, -0.08))
    out = K.args_out()
    if out is not None:
        review(part, "Respawn Bed (4 x 1 x 8 blocks)",
               ["Colony Works: a rectangular steel-blue quilted sleeping bag on a ridged graphite foam mat, its top flap",
                "folded back to the bone lining, a graphite zip down the right side with a mustard pull, a rolled stuff sack",
                "at the head with a mustard drawcord, a bone label on the foot. No frame: the Fancy Bed is the real bed."],
               rig, icon, info, out, HALF)


if __name__ == "__main__":
    main()
