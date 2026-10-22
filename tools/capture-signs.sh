#!/usr/bin/env bash
# Puts the three signs that take words in a meadow, writes on them and shoots them, then each POI kind's nearest world sign (see game/tools/capture/CaptureScene.Signs.cs).
# Usage: tools/capture-signs.sh OUT_DIR [meadow]   (meadow: only the three signs in the meadow)
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
  res://tools/capture/CaptureScene.tscn -- "$out" signs ${2:+"$2"}
