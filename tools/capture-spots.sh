#!/usr/bin/env bash
# Renders the spots listed in SPOTS_FILE ("name x z heading" a line)
# on seed 1, at eye height and from 25 m up, to compare the land before and after a change (see game/tools/capture/CaptureScene.cs). Usage: tools/capture-spots.sh OUT_DIR SPOTS_FILE
# Needs a display; without one it uses xvfb-run. Uses $GODOT if set, otherwise the pinned build.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
out="$(mkdir -p "${1:?usage: $0 OUT_DIR SPOTS_FILE}" && cd "$1" && pwd)"
spots="$(cd "$(dirname "${2:?usage: $0 OUT_DIR SPOTS_FILE}")" && pwd)/$(basename "$2")"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
run=()
[ -z "${DISPLAY:-}" ] && run=(xvfb-run -a -s "-screen 0 1280x720x24")
"${run[@]}" "$godot" --rendering-driver opengl3 --rendering-method gl_compatibility --path "$root/game" \
  res://tools/capture/CaptureScene.tscn -- "$out" spots "$spots"
