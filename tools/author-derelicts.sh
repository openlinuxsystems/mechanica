#!/usr/bin/env bash
# Writes the vanilla derelict vehicles (mods/vanilla/derelicts/*.json) from their designs in
# game/tools/derelicts/DerelictAuthor.cs. Re-run after changing a design or the shape of a part they use.
# Uses $GODOT if set, otherwise the pinned build.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
dotnet build "$root/Mechanica.sln" -c Debug >/dev/null
"$godot" --headless --path "$root/game" --import >/dev/null 2>&1
"$godot" --headless --path "$root/game" res://tools/derelicts/DerelictAuthor.tscn 2>&1 | grep -E '^\[derelicts\]|ERROR: \[derelicts\]'
