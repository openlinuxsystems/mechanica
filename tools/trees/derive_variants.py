#!/usr/bin/env python3
"""Derives the autumn and burnt tree models from the Leafy tree (World Map spec biomes: Autumn and Burned Forest).

Usage: tools/trees/derive_variants.py   (run after tools/trees/build_trees.py; needs only Python 3)

* ``autumn_leafy_tree(_lod).glb`` - the Leafy tree with orange and red leaves.
* ``burnt_tree(_lod).glb`` - the Leafy tree's trunk and branches, charred black, with no leaves.

Only the glTF JSON chunk changes (material colours, and which primitives the mesh draws), so the geometry stays the
build script's. Deterministic: the same inputs give the same bytes.
"""
import json
import os
import struct

WORLD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "mods", "vanilla", "meshes", "world")


def linear(hex_colour):
    """#rrggbb as linear RGB (glTF base colours are linear)."""
    c = [int(hex_colour[i:i + 2], 16) / 255.0 for i in (1, 3, 5)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]


AUTUMN = {"leafy_a": "#f29a1d", "leafy_b": "#e2611b", "leafy_c": "#c2401c"}
CHARRED = "#2a2522"


def read(path):
    data = open(path, "rb").read()
    length = struct.unpack("<I", data[12:16])[0]
    doc = json.loads(data[20:20 + length])
    return doc, data[20 + length:]


def write(path, doc, rest):
    text = json.dumps(doc, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    body = struct.pack("<I", len(text)) + b"JSON" + text + rest
    open(path, "wb").write(b"glTF" + struct.pack("<II", 2, 12 + len(body)) + body)


def autumn(doc):
    for m in doc["materials"]:
        if m["name"] in AUTUMN:
            m["name"] = "autumn_" + m["name"]
            m["pbrMetallicRoughness"]["baseColorFactor"] = linear(AUTUMN[m["name"][7:]]) + [1]
    return doc


def burnt(doc):
    leaves = {i for i, m in enumerate(doc["materials"]) if m["name"].startswith("leafy_")}
    for mesh in doc["meshes"]:
        mesh["primitives"] = [p for p in mesh["primitives"] if p.get("material") not in leaves]
    for m in doc["materials"]:
        if m["name"] == "bark":
            m["name"] = "charred_bark"
            m["pbrMetallicRoughness"]["baseColorFactor"] = linear(CHARRED) + [1]
    return doc


def main():
    for suffix in ("", "_lod"):
        source = os.path.join(WORLD, f"leafy_tree{suffix}.glb")
        for name, change in (("autumn_leafy_tree", autumn), ("burnt_tree", burnt)):
            doc, rest = read(source)
            write(os.path.join(WORLD, f"{name}{suffix}.glb"), change(doc), rest)
            print(f"wrote {name}{suffix}.glb")


if __name__ == "__main__":
    main()
