#!/usr/bin/env bash
# Regenerates docs/ai/reference.md and docs/ai/describe.json from the game's own mechanica.describe.
# Run it after adding, renaming or re-documenting an action or AI endpoint method; the headless
# test game/tests/aidocs fails until the checked-in copies match.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
dotnet build "$root/Mechanica.sln" -c Debug
"$godot" --headless --path "$root/game" --import >/dev/null
"$godot" --headless --path "$root/game" res://tests/aidocs/AiDocsTest.tscn -- --write
