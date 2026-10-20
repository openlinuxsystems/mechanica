"""Build the tomato's game files (Food design thread, approved by Chase 2026-10-20).

What it writes (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/tomato.glb`` - the tomato on the ground or placed (8.8 cm, origin at its base).
* ``mods/vanilla/meshes/items/tomato_held.glb`` - the tomato in the hand: ``eat_0`` (whole), ``eat_1`` and ``eat_2``
  (after the first and second bite), which the game swaps while the eat gesture plays.
* ``mods/vanilla/meshes/items/tomato_seed.glb`` - the seed packet every crop shares, with the tomato label.
* ``mods/vanilla/meshes/crops/tomato_plant.glb`` - the plant with its ``grow``, ``wilt`` and ``sway`` clips
  (see plant_rig.py), stake and crop tag included.
* ``mods/vanilla/textures/items/tomato_icon.png`` and ``tomato_seed_icon.png`` - 256 x 256 icons.
* the ``held`` offsets and rotations for tomato.json and tomato_seed.json, printed (they go in the survival batch).

    pip install bpy==5.2.2 numpy pillow
    python tools/food/build_tomato.py

The outputs are committed; re-run this script after changing it and commit the result.
"""

from __future__ import annotations

import json
import math
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "characters"))

import bpy  # noqa: E402,I001
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import food_lib as fl  # noqa: E402
import plant_rig  # noqa: E402
import seed_packet as sp  # noqa: E402
import tomato  # noqa: E402

REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
LABEL_COLOR = "#C8423A"            # the tomato's crop colour on its seed label and stake tag
ICON_VIEW = (1.25, -1.6, 1.5)
BITES = (1.2, 2.4)                 # where the two bites go round the fruit (radians from +X): toward the mouth in hand
TOMATO_H = 0.07


def export(objects, path, animations=False):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.select_set(True)
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
        export_animations=animations, export_animation_mode="NLA_TRACKS", export_force_sampling=False,
        export_optimize_animation_size=False, export_anim_slide_to_zero=False, export_def_bones=False,
        export_morph=False, export_skins=False, export_draco_mesh_compression_enable=False, export_tangents=False,
        export_image_format="AUTO", export_extras=False, export_cameras=False, export_lights=False)
    print(f"wrote {path.relative_to(REPO)} ({path.stat().st_size // 1024} KiB)")


def bitten(name, n):
    t = tomato.build(name)
    for k, a in enumerate(BITES[:n]):
        tomato.bite(t, (0.044 * math.cos(a), 0.044 * math.sin(a), 0.05 - 0.005 * k), name=f"{name}Bite{k}")
    return t


# ---------------------------------------------------------------------------------------------------------------------
# Where the held models sit in the right hand, in the hand bone's frame (glTF, Y up), from the character's rest pose

C = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))       # Blender (Z up) -> glTF (Y up)


def hand_rest_gltf():
    """The world rest transform of the hand_r joint in scrapper_male.glb, in glTF space."""
    data = (REPO / "mods" / "vanilla" / "meshes" / "characters" / "scrapper_male.glb").read_bytes()
    length = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20 + length])
    nodes = doc["nodes"]
    parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}

    def local(n):
        if "matrix" in n:
            return Matrix([n["matrix"][i::4] for i in range(4)])
        t = Matrix.Translation(n.get("translation", (0, 0, 0)))
        x, y, z, w = n.get("rotation", (0, 0, 0, 1))
        from mathutils import Quaternion
        r = Quaternion((w, x, y, z)).to_matrix().to_4x4()
        s = Matrix.Diagonal((*n.get("scale", (1, 1, 1)), 1))
        return t @ r @ s

    i = next(k for k, n in enumerate(nodes) if n.get("name") == "hand_r")
    m = Matrix.Identity(4)
    while i is not None:
        m = local(nodes[i]) @ m
        i = parent.get(i)
    return m


def held_entry(placement_blender):
    """The part data ``held`` offset and rotation that put a model placed at ``placement_blender`` (rest pose, Blender
    world) in the hand: the game parents it to the hand bone and turns it X, then Y, then Z (Godot's XYZ order)."""
    t = hand_rest_gltf().inverted() @ C @ placement_blender @ C.inverted()
    loc, rot, _ = t.decompose()
    e = rot.to_matrix().to_euler("ZYX")      # R = Rx @ Ry @ Rz, as Godot's Basis.FromEuler(..., EulerOrder.Xyz)
    return {"offset": [round(v, 4) for v in loc], "rotation": [round(math.degrees(a), 2) for a in e]}


def rest_hand():
    import build_characters as bc
    d, w, n = (Vector(v) for v in bc.hand_frame("r"))
    return Vector(bc.grip_point("r")), d, w, n


def tomato_placement():
    grip, d, w, n = rest_hand()
    centre = grip + n * 0.034 + d * 0.004
    return Matrix.Translation(centre) @ Matrix.Rotation(math.pi, 4, "X") @ Matrix.Translation((0, 0, -TOMATO_H / 2))


def packet_placement():
    # the packet's base in the fist, standing up out of the thumb side, its label away from the palm
    grip, d, w, n = rest_hand()
    z = w.normalized()
    y = (n - z * n.dot(z)).normalized()
    x = y.cross(z)
    basis = Matrix((x, y, z)).transposed().to_4x4()
    return Matrix.Translation(grip - z * 0.035) @ basis


# ---------------------------------------------------------------------------------------------------------------------

def main():
    fl.reset()
    whole = tomato.build("Tomato")
    export(fl.tree(whole), MESHES / "items" / "tomato.glb")
    fl.auto_icon(fl.tree(whole), ICONS / "tomato_icon.png", ICON_VIEW, fill=1.2)
    fl.reset()
    held = fl.empty("TomatoHeld")
    for k in range(3):
        stage = bitten(f"eat_{k}", k)
        stage.parent = held
    export(fl.tree(held), MESHES / "items" / "tomato_held.glb")

    fl.reset()
    import tempfile
    label = Path(tempfile.mkdtemp()) / "label_tomato.png"
    sp.label_image(ICONS / "tomato_icon.png", LABEL_COLOR, label)
    packet = sp.build("TomatoSeed", label)
    export(fl.tree(packet), MESHES / "items" / "tomato_seed.glb")
    fl.auto_icon(fl.tree(packet), ICONS / "tomato_seed_icon.png", (1.0, -1.7, 1.0), fill=1.15)

    fl.reset()
    rig = plant_rig.build(label)
    plant_rig.bake(rig)
    export(fl.tree(rig.root), MESHES / "crops" / "tomato_plant.glb", animations=True)

    print("held tomato:", json.dumps(held_entry(tomato_placement())))
    print("held tomato_seed:", json.dumps(held_entry(packet_placement())))


if __name__ == "__main__":
    main()
