"""Builds the Logic Gate (audit batch D2; tools/logic/logic_parts.py has the shared logic cube).

    python3 tools/logic/build_logic_gate.py [--render OUT_DIR]

The logic cube it shares with the Timer, its deck a small square screen that shows the gate's mode: the six mode
pictures tools/icons/gate_icons.py draws for the part menu, in the logic teal. Writes
``mods/vanilla/meshes/parts/logic_gate.glb`` with its clips and ``mods/vanilla/textures/items/logic_gate_icon.png``;
with ``--render`` the review sheet. Clips (played from ``display.clips``; scripts/logic_gate.lua sets the readouts):

* ``and``, ``or``, ``xor``, ``nand``, ``nor``, ``xnor`` (0.12 s each): the screen shows that mode (its ``face``
  readout);
* ``lit`` (scrubbed by the ``lit`` readout, 0 or 1): the frame round the screen lights cyan while the output is on.

Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.dont_write_bytecode = True                      # no __pycache__ next to the scripts
sys.path.insert(0, str(Path(__file__).resolve().parent))
import logic_parts as P  # noqa: E402  (first: it imports bpy)

K, Geo, box = P.K, P.Geo, P.box
SCREEN = 0.058              # half width of the screen


def frame_glow(half, t=0.0024):
    g = Geo()
    for s in (-1, 1):
        box(g, (0, s * half, 0), (half + t, t, 0.0006), "cyan_glow")
        box(g, (s * half, 0, 0), (t, half - t, 0.0006), "cyan_glow")
    return g


def build(mats):
    rig, g = P.build_cube(mats, "LogicGate", "code_gate")
    root = rig["root"]
    top = P.CASE_TOP
    P.deck_bezel_square(g, SCREEN + 0.004)
    K.make_object("Body", g, mats, root)
    faces = {}
    for mode in P.GATE_MODES:
        fg = Geo()
        # the picture reads from the front: its top towards the back (-y), its inputs on the viewer's left (+x)
        P.decal_quad(fg, (0, 0, P.SINK + 0.0018), (-1, 0, 0), (0, -1, 0), 2 * SCREEN, 2 * SCREEN, f"gate_{mode}")
        faces[mode] = K.make_object(f"Face_{mode}", fg, mats, root, (0, 0, top - P.SINK))
    glow = K.make_object("FrameGlow", P.sunk(frame_glow, SCREEN + 0.0028, offset=(0, 0, P.SINK + 0.0024)), mats, root,
                         (0, 0, top - P.SINK))
    rig.update(faces=faces, glows=[glow])

    def show(mode, u=1.0):
        for m, ob in faces.items():
            # the others shrink from just above "off" so each clip keys every face (the exporter drops a channel
            # whose keys never change, which would leave the last mode showing)
            P.lit([ob], u if m == mode else (1 - u) * P.OFF)

    for mode in P.GATE_MODES:
        P.bake_clip(list(faces.values()), mode, 0.12, lambda t, mode=mode: show(mode, K.smooth(t)))
    P.bake_clip([glow], "lit", 1.0, lambda t: P.lit([glow], t))
    P.end_clips([*faces.values(), glow])
    show("and")
    P.lit([glow], 0)

    def state(mode, on):
        def apply():
            show(mode)
            P.lit([glow], 1 if on else 0)
        return apply

    rig["states"] = {f"{m.upper()}{', output on' if on else ''}": state(m, on)
                     for m, on in (("and", False), ("or", True), ("xor", False), ("nand", True), ("nor", False), ("xnor", True))}
    rig["states"]["icon"] = state("and", True)
    return rig


if __name__ == "__main__":
    P.run("logic_gate", build, ortho=0.33, icon_target=(0, 0, -0.03), icon_state="icon", title="Logic Gate (1 block)",
          lines=["The logic cube shared with the Timer, its deck a screen showing the mode picture (and, or, xor, nand,",
                 "nor, xnor: the part menu's own). The frame round the screen lights cyan while the output is on."])
