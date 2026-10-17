#!/usr/bin/env bash
# Installs the pinned Godot .NET export templates (Linux and Windows x86_64, macOS universal) where Godot looks for them.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
source "$root/tools/versions.env"
dir="${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/${GODOT_VERSION}.stable.mono"
if [[ ! -f "$dir/linux_release.x86_64" || ! -f "$dir/windows_release_x86_64.exe" || ! -f "$dir/macos.zip" ]]; then
  mkdir -p "$root/.tools" "$dir"
  tpz="$root/.tools/godot_templates_${GODOT_VERSION}.tpz"
  [[ -f "$tpz" ]] || curl -fsSL "https://downloads.godotengine.org/?version=${GODOT_VERSION}&flavor=stable&slug=mono_export_templates.tpz" -o "$tpz"
  unzip -q -j -o "$tpz" templates/version.txt templates/icudt_godot.dat templates/linux_release.x86_64 \
    templates/windows_release_x86_64.exe templates/windows_release_x86_64_console.exe templates/macos.zip -d "$dir"
  rm -f "$tpz"
fi
echo "$dir"
