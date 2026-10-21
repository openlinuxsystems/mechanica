"""Review renders of the decor parts (build_decor.py --render OUT_DIR), with the furniture containers' layout
(tools/containers/render_furniture.py): for each part three-quarter views from the front right and the back left,
front and top views with the part's grid box drawn round them and the icon, in <id>_review_sheet.png, then
lineup.png with the batch standing in a row beside a 25 cm block. Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from types import SimpleNamespace

import render_furniture as rf

import decor_kit as D


def render_all(registry, roots, infos, out, title):
    rf.NOTES.clear()
    for pid, part in registry.items():
        x, y, z = part.size
        i = infos.get(pid)
        tris = f" {i['tris']} triangles." if i else ""
        rf.NOTES[pid] = f"{x} x {y} x {z} blocks ({x * 25} x {y * 25} x {z * 25} cm).{tris} {part.note}"
    shim = SimpleNamespace(half_size=lambda pid: registry[pid].half, icon_framing=lambda pid: (D.ICON_VIEW, D.icon_framing(registry[pid].half)),
                           gc=D.gc, ICONS=D.ICONS)
    rf.render_all(roots, out, shim)
