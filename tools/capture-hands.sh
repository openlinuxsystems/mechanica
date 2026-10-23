#!/usr/bin/env bash
# Close-ups of the player's hands holding every prop (see game/tools/capture/CaptureScene.Hands.cs): OUT_DIR/hands_*.png.
# Usage: tools/capture-hands.sh OUT_DIR [prop,...]   (CAPTURE_ONLY=text keeps matching shots)
# Needs a display; without one it uses xvfb-run. Uses $GODOT if set, otherwise the pinned build.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
out="$(mkdir -p "${1:?usage: $0 OUT_DIR [prop,...]}" && cd "$1" && pwd)"
filter="${2:-}"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
run=()
[ -z "${DISPLAY:-}" ] && run=(xvfb-run -a -s "-screen 0 1280x720x24")
"${run[@]}" "$godot" --rendering-driver opengl3 --rendering-method gl_compatibility --path "$root/game" \
  res://tools/capture/CaptureScene.tscn -- "$out" hands $filter
