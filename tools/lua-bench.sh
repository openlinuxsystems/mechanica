#!/usr/bin/env bash
# The D2 Lua benchmark: the Lua side alone (no Godot), then the scripted glider in Godot with Jolt.
# --fixed-fps makes each physics step run as fast as it can, so the step times are real costs.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
dotnet run -c Release --project "$root/bench/Mechanica.Scripting.Bench" -- "$@"
dotnet build "$root/Mechanica.sln" -c Debug >/dev/null
"$godot" --headless --path "$root/game" --import >/dev/null 2>&1
"$godot" --headless --fixed-fps 60 --path "$root/game" res://tests/luabench/LuaBenchTest.tscn | grep -E 'RESULT|FAIL'
