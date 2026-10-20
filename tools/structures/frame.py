"""Framed construction: the part-model look (Fabricator, engines, Portable Fabricator) at building scale."""
import math

import bpy
from mathutils import Vector

import kit as K


def screw(P, loc, rot, r=0.05, mat=None):
    K.cyl("screw", r, 0.03, mat, loc=loc, rot=rot, segs=10, bev=0.008, parent=P)


def framed_box(P, name, center, size, panel, frame, fw=0.16, inset=0.04, screws=None, faces="xyz", bev=0.025):
    """A box of inset panels inside bevelled edge rails with corner blocks, like the engines' cases."""
    cx, cy, cz = center
    sx, sy, sz = size
    K.box(f"{name}_core", (sx - 2 * inset, sy - 2 * inset, sz - 2 * inset), panel, loc=(cx, cy, cz), bev=0.01, parent=P)
    hx, hy, hz = sx / 2 - fw / 2, sy / 2 - fw / 2, sz / 2 - fw / 2
    for a in (-1, 1):
        for b in (-1, 1):
            K.box(f"{name}_rx", (sx - fw * 0.2, fw, fw), frame, loc=(cx, cy + a * hy, cz + b * hz), bev=bev, parent=P)
            K.box(f"{name}_ry", (fw, sy - fw * 0.2, fw), frame, loc=(cx + a * hx, cy, cz + b * hz), bev=bev, parent=P)
            K.box(f"{name}_rz", (fw, fw, sz - fw * 0.2), frame, loc=(cx + a * hx, cy + b * hy, cz), bev=bev, parent=P)
    for a in (-1, 1):
        for b in (-1, 1):
            for c in (-1, 1):
                K.box(f"{name}_corner", (fw * 1.25, fw * 1.25, fw * 1.25), frame, loc=(cx + a * hx, cy + b * hy, cz + c * hz), bev=bev * 1.3, parent=P)
    if screws is not None:
        # screws in the frame rails on the front face (-y)
        for a in (-1, 1):
            for t in (0.25, 0.75):
                screw(P, (cx + a * hx, cy - sy / 2 - 0.005, cz - sz / 2 + sz * t), (90, 0, 0), r=fw * 0.22, mat=screws)


def vent(P, loc, w, h, frame, slat, axis="y", n=None):
    """A louvred vent: frame and slats (the engines' grilles), on a face pointing -y (or -x)."""
    x, y, z = loc
    n = n or max(3, int(h / 0.12))
    if axis == "y":
        K.box("vent_frame", (w, 0.06, h), frame, loc=(x, y, z), bev=0.015, parent=P)
        for k in range(n):
            K.box("slat", (w * 0.86, 0.05, h / n * 0.45), slat, loc=(x, y - 0.03, z - h / 2 + h * (k + 0.5) / n), rot=(20, 0, 0), bev=0.005, parent=P)
    else:
        K.box("vent_frame", (0.06, w, h), frame, loc=(x, y, z), bev=0.015, parent=P)
        for k in range(n):
            K.box("slat", (0.05, w * 0.86, h / n * 0.45), slat, loc=(x - 0.03, y, z - h / 2 + h * (k + 0.5) / n), rot=(0, -20, 0), bev=0.005, parent=P)


def label(P, text, loc, size, mat, rot=(90, 0, 0), extrude=0.01, align="CENTER"):
    """Stencilled text (model codes and labels like FAB-1 and SPEED)."""
    cu = bpy.data.curves.new("label", "FONT")
    cu.body = text
    cu.size = size
    cu.extrude = extrude
    cu.align_x = align
    cu.align_y = "CENTER"
    ob = bpy.data.objects.new("label", cu)
    ob.data.materials.append(mat)
    K.link(ob)
    return K.place(ob, loc, rot, P)


def screen(P, loc, w, h, frame, glow_mat, rot=(0, 0, 0)):
    x, y, z = loc
    K.box("screen_frame", (w + 0.12, 0.08, h + 0.12), frame, loc=(x, y + 0.02, z), rot=rot, bev=0.02, parent=P)
    K.box("screen", (w, 0.06, h), glow_mat, loc=(x, y - 0.01, z), rot=rot, bev=0.01, parent=P)


def hazard(P, loc, w, h, a_mat, b_mat, n=6, axis="y", angle=35):
    x, y, z = loc
    step = w / n
    for k in range(n):
        m = a_mat if k % 2 == 0 else b_mat
        if axis == "y":
            K.box("haz", (step * 1.05, 0.04, h), m, loc=(x - w / 2 + step * (k + 0.5), y, z), bev=0.004, parent=P)
        else:
            K.box("haz", (0.04, step * 1.05, h), m, loc=(x, y - w / 2 + step * (k + 0.5), z), bev=0.004, parent=P)
