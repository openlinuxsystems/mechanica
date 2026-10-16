#!/usr/bin/env bash
# Builds the solution and runs every headless Godot test scene (game/tests/*/*.tscn).
# Uses $GODOT if set, otherwise downloads the pinned build via install-godot.sh.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
for scene in "$root"/game/tests/*/*.tscn; do
  res="res://${scene#"$root/game/"}"
  echo "== $res"
  "$godot" --headless --path "$root/game" "$res"
done
