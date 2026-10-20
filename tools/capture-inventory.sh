#!/usr/bin/env bash
# Pictures of the Inventory window in a running world: every tab, a search, Organize, a chest, the Fabricator's tabs and its upgrade dialog
# (see game/tools/capture/InventoryCapture.cs). Usage: tools/capture-inventory.sh OUT_DIR [creative]
# Needs a display; without one it uses xvfb-run. Uses $GODOT if set, otherwise the pinned build.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
out="$(mkdir -p "${1:?usage: $0 OUT_DIR [creative]}" && cd "$1" && pwd)"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
run=()
[ -z "${DISPLAY:-}" ] && run=(xvfb-run -a -s "-screen 0 1280x720x24")
"${run[@]}" "$godot" --rendering-driver opengl3 --rendering-method gl_compatibility --resolution 1280x720 --path "$root/game" \
  res://tools/capture/InventoryCapture.tscn -- "$out" "${2:-}"
