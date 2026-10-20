#!/usr/bin/env bash
# Renders the opening cinematic in a running world (see game/tools/capture/CaptureScene.cs): stills through it to PNGs,
# and with MOVIE=1 every frame of it (and its sound) to OUT_DIR/opening.avi at 30 frames a second.
# Usage: [MOVIE=1] tools/capture-opening.sh OUT_DIR
# Needs a display; without one it uses xvfb-run. Uses $GODOT if set, otherwise the pinned build.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
out="$(mkdir -p "${1:?usage: $0 OUT_DIR}" && cd "$1" && pwd)"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
run=()
[ -z "${DISPLAY:-}" ] && run=(xvfb-run -a -s "-screen 0 1280x720x24")
movie=()
[ "${MOVIE:-0}" = 1 ] && movie=(--write-movie "$out/opening.avi" --fixed-fps 30)
"${run[@]}" "$godot" --rendering-driver opengl3 --rendering-method gl_compatibility "${movie[@]}" --path "$root/game" \
  res://tools/capture/CaptureScene.tscn -- "$out" opening
