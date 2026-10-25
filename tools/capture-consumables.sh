#!/usr/bin/env bash
# Renders consumables batch C2b in a running world to PNGs: Bloom Tea and a Grazer Shake held and drunk, Fertilizer in hand, and a fertilized crop
# (see game/tools/capture/CaptureScene.cs). Usage: tools/capture-consumables.sh OUT_DIR [grazer_products|kitchen|crops|crops_k3|crops_k4]
# With grazer_products it renders batch C3 instead: a tame Grazer eating from its Feeding Trough until its milk is ready,
# the milk collected and held, and Grazer Steak dropped by a killed Grazer and held. With kitchen it renders batch C4: a
# Field Kitchen's station screen and cooking, and the Garden and Colony Burgers held and eaten. With crops it renders crops
# batch K2: a wild Pigment Flower, the flower and its seed packet held, and the crop at every grow stage, wilted last.
# With crops_k3 it renders batch K3: wild Cotton, Beetroot, Banana and Chili held and bitten, their seed packets, and
# a row of mounds per crop at every grow stage. With crops_k4 it renders batch K4 the same way: Blueberry, Orange, Broccoli
# and Pineapple held and bitten, their seed packets, and a row of mounds per crop.
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
  res://tools/capture/CaptureScene.tscn -- "$out" "${2:-consumables}"
