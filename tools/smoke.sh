#!/usr/bin/env bash
# Builds the solution and runs every headless Godot test scene (game/tests/*/*.tscn).
# Uses $GODOT if set, otherwise downloads the pinned build via install-godot.sh.
# SMOKE_SHARD=k/n runs only every n-th scene starting at the k-th (1-based), so CI can split the scenes
# across n runners; together the n shards run every scene exactly once.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
shard="${SMOKE_SHARD:-1/1}"
if [[ ! "$shard" =~ ^([1-9][0-9]*)/([1-9][0-9]*)$ ]] || (( BASH_REMATCH[1] > BASH_REMATCH[2] )); then
  echo "SMOKE_SHARD must be k/n with 1 <= k <= n, not '$shard'" >&2
  exit 2
fi
index=$(( BASH_REMATCH[1] - 1 )) count="${BASH_REMATCH[2]}"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import
i=0
for scene in "$root"/game/tests/*/*.tscn; do
  if (( i++ % count != index )); then continue; fi
  res="res://${scene#"$root/game/"}"
  echo "== $res"
  "$godot" --headless --path "$root/game" "$res"
done
