#!/usr/bin/env bash
# Re-makes vanilla content that is authored by Part Editor and Bot Behavior Editor batches
# (mods/vanilla/editor/batches/*.json, plus the example mods' examples/mods/*/editor/batches/*.json)
# by running each batch headless through the editor's exporter. Run it after editing a batch, then commit
# the changed parts/, meshes/, bots/, raids/ and editor/ files. Uses $GODOT if set, otherwise the pinned build.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
for batch in "$root"/mods/vanilla/editor/batches/*.json "$root"/examples/mods/*/editor/batches/*.json; do
  echo "== $batch"
  "$godot" --headless --path "$root/game" -- --part-batch "$batch"
done
