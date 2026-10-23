#!/usr/bin/env bash
# Drives each driver's seat on a slab in a running world (see game/tools/capture/CaptureScene.Driving.cs): steering
# and the pedals, seen at the hands, at the feet and from the driver's eyes. OUT_DIR/<shot>/*.png and OUT_DIR/<shot>.mp4.
# Usage: tools/capture-driving.sh OUT_DIR [seat part ids, comma-separated]
# Needs a display; without one it uses xvfb-run. Uses $GODOT if set, otherwise the pinned build. Needs ffmpeg.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
out="$(mkdir -p "${1:?usage: $0 OUT_DIR [seats]}" && cd "$1" && pwd)"
only="${2:-}"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
run=()
[ -z "${DISPLAY:-}" ] && run=(xvfb-run -a -s "-screen 0 1280x720x24")
"${run[@]}" "$godot" --rendering-driver opengl3 --rendering-method gl_compatibility --fixed-fps 30 --path "$root/game" \
  res://tools/capture/CaptureScene.tscn -- "$out" driving $only
for dir in "$out"/*/; do
  name="$(basename "$dir")"
  [ -f "$dir/0000.png" ] || continue
  ffmpeg -loglevel error -y -framerate 30 -i "$dir/%04d.png" -vf "scale=640:-2" -c:v libx264 -pix_fmt yuv420p "$out/$name.mp4"
done
