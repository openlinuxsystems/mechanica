#!/usr/bin/env bash
# Plays every clip of every vanilla bot model in a running world (see game/tools/capture/CaptureScene.BotClips.cs):
# OUT_DIR/<model>_<clip>/*.png, OUT_DIR/<model>_<clip>.gif and .mp4.
# Usage: tools/capture-bot-clips.sh OUT_DIR [model filter]   (CAPTURE_ONLY=grazer_idle keeps matching clips)
# Needs a display; without one it uses xvfb-run. Uses $GODOT if set, otherwise the pinned build. Needs ffmpeg.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
out="$(mkdir -p "${1:?usage: $0 OUT_DIR [model filter]}" && cd "$1" && pwd)"
filter="${2:-}"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
run=()
[ -z "${DISPLAY:-}" ] && run=(xvfb-run -a -s "-screen 0 1280x720x24")
"${run[@]}" "$godot" --rendering-driver opengl3 --rendering-method gl_compatibility --fixed-fps 30 --path "$root/game" \
  res://tools/capture/CaptureScene.tscn -- "$out" bot_clips $filter
for dir in "$out"/*/; do
  name="$(basename "$dir")"
  [ -f "$dir/0000.png" ] || continue
  ffmpeg -loglevel error -y -framerate 30 -i "$dir/%04d.png" -vf "crop=720:720:280:0,scale=480:-2" -c:v libx264 -pix_fmt yuv420p "$out/$name.mp4"
done
