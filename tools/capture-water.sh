#!/usr/bin/env bash
# Renders lakes and the sea in a running world to PNGs: a shore with the sun across the water, every Water quality
# level, a run of frames for the motion and the view from under the surface (see game/tools/capture/CaptureScene.cs).
# Usage: tools/capture-water.sh OUT_DIR [quick]  (quick: only the first few views, for tuning)
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
  res://tools/capture/CaptureScene.tscn -- "$out" water ${2:-}
