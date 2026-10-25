"""Builds the Timer (audit batch D2; tools/logic/logic_parts.py has the shared logic cube).

    python3 tools/logic/build_timer.py [--render OUT_DIR]

The logic cube it shares with the Logic Gate, its deck a round dial screen in a gunmetal bezel: teal ticks and a
sweep hand on a copper hub, 12 o'clock towards the back. Writes ``mods/vanilla/meshes/parts/timer.glb`` with its
clips and ``mods/vanilla/textures/items/timer_icon.png``; with ``--render`` the review sheet. Clips (played from
``display.clips``; scripts/timer.lua sets the readouts):

* ``count_loop`` (2 s): the hand sweeps round while a change waits to come out (once) or while it blinks (repeat);
* ``rest`` (0.12 s): the hand back at 12 o'clock;
* ``lit`` (scrubbed by the ``lit`` readout, 0 or 1): the hand and the ring round the dial light cyan while the output
  is on.

Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import math
import sys
from pathlib import Path

sys.dont_write_bytecode = True                      # no __pycache__ next to the scripts
sys.path.insert(0, str(Path(__file__).resolve().parent))
import logic_parts as P  # noqa: E402  (first: it imports bpy)

K, Geo, box, cylinder, ring = P.K, P.Geo, P.box, P.cylinder, P.ring
DIAL_R = 0.066
HAND_Z = P.CASE_TOP + 0.0034


def build(mats):
    rig, g = P.build_cube(mats, "Timer", "code_timer")
    root, f = rig["root"], rig["finish"]
    top = P.CASE_TOP
    # the dial: a dark round screen in a raised gunmetal bezel, a teal groove round it
    ring(g, top - 0.001, top + 0.007, DIAL_R, DIAL_R + 0.01, "gun", segs=48)
    ring(g, top - 0.001, top + 0.0045, DIAL_R + 0.01, DIAL_R + 0.0135, "teal", segs=48)
    ring(g, top - 0.001, top + 0.004, DIAL_R + 0.0135, DIAL_R + 0.016, "gun_dark", segs=48)
    P.uv_disc(g, (0, 0, top + 0.002), DIAL_R, "timer_dial", segs=48)
    K.make_object("Body", g, mats, root)

    hand = K.make_empty("Hand", root, (0, 0, HAND_Z))
    hg = Geo()
    box(hg, (0, -0.026, 0.0008), (0.0026, 0.03, 0.0008), "teal", bevel=0.0006)       # towards 12 o'clock (-y)
    box(hg, (0, 0.012, 0.0008), (0.0034, 0.008, 0.0008), "teal", bevel=0.0006)       # the tail
    cylinder(hg, (0, 0, -0.0015), (0, 0, 0.0042), 0.0068, f["trim"], segs=16)        # the copper hub
    cylinder(hg, (0, 0, 0.0042), (0, 0, 0.0052), 0.0034, "hole", segs=12)
    K.make_object("HandBody", hg, mats, hand)
    glows = [
        K.make_object("HandGlow", P.sunk(K.glow_box, (0.0018, 0.029, 0.0005), "cyan_glow", offset=(0, -0.026, P.SINK + 0.0021)),
                      mats, hand, (0, 0, -P.SINK)),
        K.make_object("RingGlow", P.sunk(K.glow_ring, DIAL_R + 0.0105, DIAL_R + 0.013, "cyan_glow", h=0.0012,
                                         offset=(0, 0, P.SINK + 0.0048)), mats, root, (0, 0, top - P.SINK)),
    ]
    rig.update(hand=hand, glows=glows)

    def turn(deg):
        hand.rotation_mode = "XYZ"
        hand.rotation_euler = (0, 0, math.radians(deg))

    # clips: the hand's two (played) and the glows' one (scrubbed), on disjoint nodes
    P.bake_clip([hand], "count_loop", 2.0, lambda t: turn(-360.0 * t))
    P.bake_clip([hand], "rest", 0.12, lambda t: turn(-0.05 * (1 - K.smooth(t))))
    P.bake_clip(glows, "lit", 1.0, lambda t: P.lit(glows, t))
    P.end_clips([hand, *glows])
    turn(0)
    P.lit(glows, 0)

    def state(deg, on):
        def apply():
            turn(deg)
            P.lit(glows, 1 if on else 0)
        return apply

    rig["states"] = {"idle": state(0, False), "counting": state(-130, False), "output on": state(-250, True),
                     "icon": state(-60, True)}
    return rig


if __name__ == "__main__":
    P.run("timer", build, ortho=0.33, icon_target=(0, 0, -0.03), icon_state="icon", title="Timer (1 block)",
          lines=["The logic cube shared with the Logic Gate: framed gunmetal case, IN and OUT link sockets on the front,",
                 "a round dial on its deck. The hand sweeps while it counts; hand and ring light cyan while its output is on."])
