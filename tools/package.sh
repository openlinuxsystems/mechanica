#!/usr/bin/env bash
# Exports a playable build and zips it: tools/package.sh <linux|windows|macos> [version].
# The zip holds the game, the bundled .NET runtime, mods/ (vanilla content) and docs/ where exported builds
# look for them (ModPaths.ShippedFolder): next to the executable, or inside Mechanica.app/Contents/Resources
# on macOS, whose app is then ad-hoc signed with rcodesign. Output: build/Mechanica-<version>-<platform>.zip.
# Uses $GODOT if set, otherwise the pinned build via install-godot.sh; export templates via install-godot-templates.sh.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
platform="${1:?usage: tools/package.sh <linux|windows|macos> [version]}"
version="${2:-$(grep -oP 'Version = "\K[^"]+' "$root/src/Mechanica.Core/MechanicaInfo.cs")}"
case "$platform" in
  linux) preset="Linux"; exe="Mechanica.x86_64" ;;
  windows) preset="Windows"; exe="Mechanica.exe" ;;
  macos) preset="macOS"; exe="Mechanica.zip" ;;
  *) echo "unknown platform '$platform' (linux, windows or macos)" >&2; exit 2 ;;
esac
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
"$root/tools/install-godot-templates.sh" >/dev/null

name="Mechanica-$version-$platform"
out="$root/build/$name"
rm -rf "$out" "$root/build/$platform" "$out.zip"
mkdir -p "$root/build/$platform"
# Build first so the editor loads the game's scripts (autoloads) before it exports.
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
"$godot" --headless --path "$root/game" --export-release "$preset" "$root/build/$platform/$exe"
# Godot reports a failed .NET publish as a warning and still writes the binary, so check for the assemblies.
if [[ "$platform" == macos ]]; then
  assemblies="$(unzip -l "$root/build/$platform/$exe" | grep -c 'data_Mechanica.Game_macos_.*/Mechanica.Game.dll' || true)"
else
  assemblies="$(find "$root/build/$platform" -path '*/data_Mechanica.Game_*/Mechanica.Game.dll' | wc -l)"
fi
if [[ ! -f "$root/build/$platform/$exe" || "$assemblies" -eq 0 ]]; then
  echo "export failed: no game assemblies in the $platform build; MSBuild logs:" >&2
  find "${XDG_DATA_HOME:-$HOME/.local/share}/godot/mono/build_logs" -name msbuild_log.txt -exec grep -hE "error|warning" {} + >&2 2>/dev/null || true
  exit 1
fi
if [[ "$platform" == macos ]]; then
  # Godot exports the app bundle as a zip; the content goes inside it, then the bundle is signed (Apple Silicon
  # refuses unsigned code, and signing after adding files keeps the seal valid).
  mkdir -p "$out"
  unzip -q "$root/build/$platform/$exe" -d "$out"
  rm -rf "$root/build/$platform"
  resources="$out/Mechanica.app/Contents/Resources"
  cp -r "$root/mods" "$resources/mods"
  cp -r "$root/docs" "$resources/docs"
  "$("$root/tools/install-rcodesign.sh")" sign "$out/Mechanica.app"
else
  mv "$root/build/$platform" "$out"
  cp -r "$root/mods" "$out/mods"
  cp -r "$root/docs" "$out/docs"
  [[ "$platform" == linux ]] && chmod +x "$out/$exe"
fi
cp "$root/tools/release/README.txt" "$out/README.txt"
(cd "$root/build" && python3 -c 'import shutil,sys; shutil.make_archive(sys.argv[1], "zip", ".", sys.argv[1])' "$name")
echo "$root/build/$name.zip"
