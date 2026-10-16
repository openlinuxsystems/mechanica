#!/usr/bin/env bash
# Downloads the pinned Godot .NET build into .tools/ and prints the binary path.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
source "$root/tools/versions.env"
name="Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64"
dir="$root/.tools/$name"
bin="$dir/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64"
if [[ ! -x "$bin" ]]; then
  mkdir -p "$root/.tools"
  url="https://downloads.godotengine.org/?version=${GODOT_VERSION}&flavor=stable&slug=mono_linux_x86_64.zip"
  curl -fsSL "$url" -o "$root/.tools/godot.zip"
  unzip -q -o "$root/.tools/godot.zip" -d "$root/.tools"
  rm "$root/.tools/godot.zip"
fi
echo "$bin"
