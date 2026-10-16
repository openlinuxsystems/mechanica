#!/usr/bin/env bash
# Builds the solution and runs the headless Godot smoke scene.
# Uses $GODOT if set, otherwise downloads the pinned build via install-godot.sh.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
"$godot" --headless --path "$root/game" res://tests/smoke/Smoke.tscn
