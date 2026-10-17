#!/usr/bin/env bash
# Re-makes vanilla content that is authored by Part Editor batches (mods/vanilla/editor/batches/*.json)
# by running each batch headless through the editor's exporter. Run it after editing a batch, then commit
# the changed parts/, meshes/ and editor/ files. Uses $GODOT if set, otherwise the pinned build.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
for batch in "$root"/mods/vanilla/editor/batches/*.json; do
  echo "== $batch"
  "$godot" --headless --path "$root/game" -- --part-batch "$batch"
done
