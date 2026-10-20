"""Stand-in carrot and potato, only for showing the seed-packet language until those crops get their own design pass."""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Vector

import food_lib as fl


def carrot(name="Carrot", parent=None, location=(0, 0, 0)):
    m_body = fl.material("carrot_body", "#E07A22", rough=0.55, sss=0.05)
    m_top = fl.material("carrot_top", "#4E8E32", rough=0.6)
    root = fl.empty(name, parent, location)
    L = 0.16
    axis = Vector((math.cos(math.radians(28)), 0, -math.sin(math.radians(28))))   # lying on a slant, tip down-right
    top = Vector((0, 0, 0.085))
    pts, radii = [], []
    for i in range(25):
        s = i / 24
        pts.append(top + axis * (L * s) + Vector((0, 0.003 * math.sin(s * 4), 0)))
        r = 0.02 * (1 - s) ** 0.7 + 0.0012
        r *= 1 - 0.06 * (math.sin(s * 70) > 0.7)          # growth rings
        radii.append(r)
    fl.from_bm(name + "_Body", fl.tube(pts, radii, segs=20), [m_body], root)
    for k in range(5):
        a = -0.7 + k * 0.35
        base = top - axis * 0.004
        tip = base + Vector((-0.06 - 0.03 * math.cos(a), 0.05 * math.sin(a), 0.07 + 0.02 * math.cos(a * 2)))
        pp = fl.bezier(base, base + Vector((-0.005, 0, 0.03)), tip + Vector((0.015, 0, -0.01)), tip, n=10)
        fb = fl.strip(pp, [0.003 + 0.008 * math.sin(math.pi * i / 10) ** 0.8 for i in range(11)],
                      [(math.sin(a), -math.cos(a), 0.4)] * 11, fold=0.3)
        o = fl.from_bm(f"{name}_Leaf{k}", fb, [m_top], root)
        fl.solidify(o, 0.001)
    return root


def potato(name="Potato", parent=None, location=(0, 0, 0)):
    m = fl.material("potato_skin", "#B8925A", rough=0.8)
    root = fl.empty(name, parent, location)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=40, v_segments=24, radius=1)
    rnd = random.Random(2)
    bumps = [(Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))).normalized(), rnd.uniform(-0.08, 0.08))
             for _ in range(9)]
    eyes = [Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.6, 1))).normalized() for _ in range(6)]
    for v in bm.verts:
        n = v.co.normalized()
        r = 1.0
        for d, a in bumps:
            r += a * max(0, n.dot(d)) ** 3
        for e in eyes:
            r -= 0.06 * max(0, n.dot(e) - 0.975) / 0.025
        v.co = Vector((n.x * 0.055, n.y * 0.038, n.z * 0.033)) * r
    lo = min(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z -= lo
    fl.from_bm(name + "_Body", bm, [m], root)
    return root
