#!/usr/bin/env bash
# Renders each character in a running world to PNGs (see game/tools/capture/CaptureScene.cs).
# Usage: tools/capture-characters.sh OUT_DIR [character ...]   (default: both vanilla scrappers)
# Needs a display; without one it uses xvfb-run. Uses $GODOT if set, otherwise the pinned build.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
out="$(mkdir -p "${1:?usage: $0 OUT_DIR [character ...]}" && cd "$1" && pwd)"
shift
characters=("${@:-scrapper_male scrapper_female}")
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
run=()
[ -z "${DISPLAY:-}" ] && run=(xvfb-run -a -s "-screen 0 1280x720x24")
for character in ${characters[@]}; do
  mkdir -p "$out/$character"
  "${run[@]}" "$godot" --rendering-driver opengl3 --rendering-method gl_compatibility --path "$root/game" \
    res://tools/capture/CaptureScene.tscn -- "$out/$character" "$character"
done
