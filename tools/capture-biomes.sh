#!/usr/bin/env bash
# Renders borders between biomes in a running world to PNGs: a few pairs of neighbouring biomes near the start, each
# from above and at head height looking across the border (see game/tools/capture/CaptureScene.cs).
# Usage: tools/capture-biomes.sh OUT_DIR
# Needs a display; without one it uses xvfb-run. Uses $GODOT if set, otherwise the pinned build.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
out="$(mkdir -p "${1:?usage: $0 OUT_DIR}" && cd "$1" && pwd)"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
run=()
[ -z "${DISPLAY:-}" ] && run=(xvfb-run -a -s "-screen 0 1280x720x24")
"${run[@]}" "$godot" --rendering-driver opengl3 --rendering-method gl_compatibility --path "$root/game" \
  res://tools/capture/CaptureScene.tscn -- "$out" biomes
