#!/usr/bin/env bash
# Frame-cost benchmark: a host and a client with a dozen debug cars on the generated world
# (game/tools/perfbench). Prints mean and 95th percentile process, physics-process and frame
# times and the host's upload. --fixed-fps makes each frame one physics tick, so the times are real costs.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
dotnet build "$root/Mechanica.sln" -c Debug >/dev/null
"$godot" --headless --path "$root/game" --import >/dev/null 2>&1
"$godot" --headless --fixed-fps 60 --path "$root/game" res://tools/perfbench/PerfBench.tscn | grep -E 'RESULT|ERROR'
