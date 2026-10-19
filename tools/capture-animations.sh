#!/usr/bin/env bash
# Records the player character's moves in a running world and turns each into a clip (see
# game/tools/capture/CaptureScene.Animations.cs): OUT_DIR/<move>/*.png, OUT_DIR/<move>.gif and OUT_DIR/<move>.mp4.
# Usage: tools/capture-animations.sh OUT_DIR [character]   (default: scrapper_male; CAPTURE_ONLY=walk keeps matching moves)
# Needs a display; without one it uses xvfb-run. Uses $GODOT if set, otherwise the pinned build. Needs ffmpeg.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
out="$(mkdir -p "${1:?usage: $0 OUT_DIR [character]}" && cd "$1" && pwd)"
character="${2:-scrapper_male}"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
run=()
[ -z "${DISPLAY:-}" ] && run=(xvfb-run -a -s "-screen 0 1280x720x24")
# Every frame is 1/30 s of game time, however long the software renderer takes to draw it.
"${run[@]}" "$godot" --rendering-driver opengl3 --rendering-method gl_compatibility --fixed-fps 30 --path "$root/game" \
  res://tools/capture/CaptureScene.tscn -- "$out" animations "$character"
for dir in "$out"/*/; do
  name="$(basename "$dir")"
  [ -f "$dir/0000.png" ] || continue
  ffmpeg -loglevel error -y -framerate 30 -i "$dir/%04d.png" -vf "crop=720:720:280:0,scale=480:-2" -c:v libx264 -pix_fmt yuv420p "$out/$name.mp4"
  ffmpeg -loglevel error -y -framerate 30 -i "$dir/%04d.png" \
    -vf "crop=720:720:280:0,scale=360:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse" "$out/$name.gif"
done
