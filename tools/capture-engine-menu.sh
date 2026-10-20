#!/usr/bin/env bash
# Captures the engine menu in a running world: a Gas Engine at levels 1, 4 and 5, a Scrap Gas Engine and an Electric Engine,
# then the lever pulled and an upgrade (see game/tools/capture/CaptureScene.EngineMenu.cs). Usage: tools/capture-engine-menu.sh OUT_DIR
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
  res://tools/capture/CaptureScene.tscn -- "$out" engine_menu
