#!/usr/bin/env bash
# Exports a playable build and zips it: tools/package.sh <linux|windows> [version].
# The zip holds the game, the bundled .NET runtime, mods/ (vanilla content) and docs/ next to the
# executable, where exported builds look for them (ModPaths, HelpWindow). Output: build/Mechanica-<version>-<platform>.zip.
# Uses $GODOT if set, otherwise the pinned build via install-godot.sh; export templates via install-godot-templates.sh.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
platform="${1:?usage: tools/package.sh <linux|windows> [version]}"
version="${2:-$(grep -oP 'Version = "\K[^"]+' "$root/src/Mechanica.Core/MechanicaInfo.cs")}"
case "$platform" in
  linux) preset="Linux"; exe="Mechanica.x86_64" ;;
  windows) preset="Windows"; exe="Mechanica.exe" ;;
  *) echo "unknown platform '$platform' (linux or windows)" >&2; exit 2 ;;
esac
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
"$root/tools/install-godot-templates.sh" >/dev/null

name="Mechanica-$version-$platform"
out="$root/build/$name"
rm -rf "$out" "$root/build/$platform" "$out.zip"
mkdir -p "$root/build/$platform"
"$godot" --headless --path "$root/game" --import
"$godot" --headless --path "$root/game" --export-release "$preset" "$root/build/$platform/$exe"
test -f "$root/build/$platform/$exe"
mv "$root/build/$platform" "$out"
cp -r "$root/mods" "$out/mods"
cp -r "$root/docs" "$out/docs"
cp "$root/tools/release/README.txt" "$out/README.txt"
[[ "$platform" == linux ]] && chmod +x "$out/$exe"
(cd "$root/build" && rm -f "$name.zip" && python3 -c 'import shutil,sys; shutil.make_archive(sys.argv[1], "zip", ".", sys.argv[1])' "$name")
echo "$root/build/$name.zip"
