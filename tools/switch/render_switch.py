"""Review renders of the Switch (build_switch.py --render OUT_DIR).

Writes, into OUT_DIR: the switch off and on, three-quarter from the front right, from the front and from above.
Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "lift"))
import render_lift as rl  # noqa: E402


def render_all(rig, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.FLOOR
    for ob in bpy.context.scene.objects:
        ob.animation_data_clear()
    target = (0.0, 0.0, build.FLOOR + 0.02)
    views = {
        "three_quarter": ((0.32, -0.42, 0.3), 0.3),
        "front": ((0.0, -0.6, build.FLOOR + 0.05), 0.26),
        "top": ((0.0, -0.001, 0.6), 0.26),
    }
    for state, on in (("off", 0.0), ("on", 1.0)):
        build.apply_pose(rig, on)
        for view, (loc, ortho) in views.items():
            cam = rl.camera(f"{state}_{view}", loc, target, ortho=ortho)
            rl.render(cam, out / f"switch_{state}_{view}.png", 800, 600)
