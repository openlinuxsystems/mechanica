#!/usr/bin/env bash
# Builds the game, hosts a world with the AI endpoint open, and lets a model play it through the
# OpenAI-compatible bridge (Ollama, llama.cpp, vLLM, SGLang, LM Studio, xAI). Guide: docs/ai/connect-grok-local.md
#
#   tools/ai/play.sh --model-url http://192.168.1.50:11434/v1 --model qwen3.8:27b   # first time
#   tools/ai/play.sh                                                                 # then, same settings
#
# Settings given once are saved to .ai-play.env at the repository root (not committed) and used next time.
# Ctrl+C stops the AI; the game keeps running so you can carry on. Run with --help for every option.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
config="$root/.ai-play.env"
logs="$root/build/ai-play"

usage() {
  cat <<EOF
Usage: tools/ai/play.sh [options] [-- game options]

Model (saved for next time)
  --model-url URL   The model server's API root, e.g. http://192.168.1.50:11434/v1 (Ollama)
  --model NAME      The model name, e.g. qwen3.8:27b
  --api-key KEY     Bearer key, if the server needs one (xAI)
  --name NAME       The AI's player name (default Scout)
  --groups a,b      Tool areas to offer (default: the ones for playing; "all" for every tool)

This session
  --job TEXT        What the AI should do (default: explore, do the first quests, report bugs)
  --no-ai           Only build and host; connect an AI yourself (the token is printed)
  --no-build        Skip dotnet build
  --load FILE       Load a saved world instead of making a new one

Anything after -- goes to the game, e.g. -- --creative --seed 42
The game's log is in build/ai-play/game.log; the AI's notes in build/ai-play/<name>-notes.md.
EOF
}

MODEL_URL="" MODEL="" API_KEY="" NAME="Scout" TOOL_GROUPS="perception,remote,items,survival,quests,chat" TOKEN=""
# shellcheck disable=SC1090
[[ -f "$config" ]] && source "$config"
job="" no_ai=0 build=1 game_args=() save=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --model-url) MODEL_URL="$2"; save=1; shift 2 ;;
    --model) MODEL="$2"; save=1; shift 2 ;;
    --api-key) API_KEY="$2"; save=1; shift 2 ;;
    --name) NAME="$2"; save=1; shift 2 ;;
    --groups) TOOL_GROUPS="$2"; save=1; shift 2 ;;
    --job) job="$2"; shift 2 ;;
    --no-ai) no_ai=1; shift ;;
    --no-build) build=0; shift ;;
    --load) game_args+=(--load "$2"); shift 2 ;;
    --help|-h) usage; exit 0 ;;
    --) shift; game_args+=("$@"); break ;;
    *) echo "Unknown option: $1 (see --help)" >&2; exit 2 ;;
  esac
done

say() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

[[ -n "$TOKEN" ]] || { TOKEN="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')"; save=1; }
if [[ $save -eq 1 ]]; then
  {
    echo "# Saved by tools/ai/play.sh; edit or delete freely."
    printf 'MODEL_URL=%q\nMODEL=%q\nAPI_KEY=%q\nNAME=%q\nTOOL_GROUPS=%q\nTOKEN=%q\n' "$MODEL_URL" "$MODEL" "$API_KEY" "$NAME" "$TOOL_GROUPS" "$TOKEN"
  } > "$config"
  chmod 600 "$config"
fi
if [[ $no_ai -eq 0 && ( -z "$MODEL_URL" || -z "$MODEL" ) ]]; then
  fail "Tell me which model to use the first time, e.g. tools/ai/play.sh --model-url http://192.168.1.50:11434/v1 --model qwen3.8:27b"
fi

# Tools
command -v dotnet >/dev/null || fail "The .NET SDK is missing. Install .NET 10: https://dotnet.microsoft.com/download"
if [[ $no_ai -eq 0 ]]; then
  command -v node >/dev/null || fail "Node.js is missing. Install Node 22 or newer: https://nodejs.org"
  node_major="$(node -p 'process.versions.node.split(".")[0]')"
  [[ "$node_major" -ge 22 ]] || fail "Node $node_major is too old; the bridge needs Node 22 or newer."
fi

# Model server, checked first so a typo shows before the build.
if [[ $no_ai -eq 0 ]]; then
  say "Checking the model server at $MODEL_URL"
  auth=()
  [[ -n "$API_KEY" ]] && auth=(-H "Authorization: Bearer $API_KEY")
  if ! models="$(curl -fsS --max-time 10 "${auth[@]}" "${MODEL_URL%/}/models" 2>&1)"; then
    fail "Can't reach $MODEL_URL ($models). Is the server running, and is its port open to this machine?"
  fi
  grep -q "\"$MODEL\"" <<<"$models" || echo "    Warning: the server doesn't list '$MODEL'. Models it has: $(grep -o '"id":"[^"]*"' <<<"$models" | cut -d'"' -f4 | tr '\n' ' ')"
fi

# Build
mkdir -p "$logs"
if [[ $build -eq 1 ]]; then
  say "Building the game"
  dotnet build "$root/Mechanica.sln" -c Debug -v quiet -nologo > "$logs/build.log" 2>&1 || { tail -30 "$logs/build.log"; fail "The build failed (full log: $logs/build.log)"; }
fi
say "Getting Godot"
godot="${GODOT:-$("$root/tools/install-godot.sh")}"
if [[ ! -d "$root/game/.godot" ]]; then
  say "Importing the game's assets (first run only)"
  "$godot" --headless --path "$root/game" --import > "$logs/import.log" 2>&1 || true
fi

# Host
port=27461
if (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
  fail "Something already listens on port $port; is the game already running? Close it, or connect with --no-ai."
fi
say "Starting the game (log: $logs/game.log)"
"$godot" --path "$root/game" -- --host --ai "$port" --ai-token "$TOKEN" --name "${USER:-Host}" "${game_args[@]}" > "$logs/game.log" 2>&1 &
game_pid=$!
for _ in $(seq 1 180); do
  kill -0 "$game_pid" 2>/dev/null || { tail -20 "$logs/game.log"; fail "The game quit while starting (log: $logs/game.log)"; }
  (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null && break
  sleep 1
done
(exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null || fail "The game didn't open the AI endpoint within 3 minutes (log: $logs/game.log)"
say "The world is up; the AI endpoint is ws://127.0.0.1:$port"

if [[ $no_ai -eq 1 ]]; then
  echo "    Token: $TOKEN"
  echo "    Connect an AI, e.g.: node tools/ai/bridge/openai-bridge.mjs --base-url URL --model NAME --token $TOKEN"
  wait "$game_pid"
  exit 0
fi

# AI
say "$NAME joins the world. Press V in the game to watch it, Ctrl+C here to stop it."
bridge=(node "$root/tools/ai/bridge/openai-bridge.mjs" --base-url "$MODEL_URL" --model "$MODEL" --token "$TOKEN" --name "$NAME"
  --notes "$logs/$NAME-notes.md" --transcript "$logs/$NAME-session.jsonl")
[[ "$TOOL_GROUPS" != "all" && -n "$TOOL_GROUPS" ]] && bridge+=(--groups "$TOOL_GROUPS")
[[ -n "$job" ]] && bridge+=(--job "$job")
[[ -n "$API_KEY" ]] && export OPENAI_API_KEY="$API_KEY"
trap 'echo; say "Stopped the AI. The game keeps running; close its window when you are done."' INT
"${bridge[@]}" || true
trap - INT
say "Notes and bug reports: $logs/$NAME-notes.md"
kill -0 "$game_pid" 2>/dev/null && echo "    The game is still running (close its window to quit)."
