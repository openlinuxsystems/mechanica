# Connecting Grok running locally

This guide runs xAI's open Grok 2 weights on your own machine and lets it play and mod Mechanica. The
same steps work for any model behind an OpenAI-compatible chat API: another local model on llama.cpp,
Ollama, vLLM or LM Studio, or Grok through xAI's hosted API (see the end).

How it fits together:

```
Grok (SGLang, OpenAI-compatible API)  <->  tools/ai/bridge/openai-bridge.mjs  <->  game host (--ai endpoint)
```

The bridge reads the game's methods from `mechanica.describe`, offers them to the model as tools, runs
the calls the model makes, feeds back results and game events, and saves the model's notes and bug
reports to a file. It needs Node 22 or newer and no packages.

## Quick start: one script

Once a model server is running (step 1, or Ollama below), `tools/ai/play.sh` does the rest on a Linux
machine with the .NET 10 SDK and Node 22: it checks the model server, builds the game, downloads the
pinned Godot, hosts a world with the AI endpoint and a fresh token, and starts the bridge so the AI joins.

```sh
tools/ai/play.sh --model-url http://192.168.1.137:11434/v1 --model qwen3.8:27b   # first time
tools/ai/play.sh                                                                  # later: same settings
tools/ai/play.sh --job "Build a small car and report anything odd about the physics"
```

It saves the model settings and token to `.ai-play.env` (not committed). The AI starts with the tools
for playing (`--groups all` offers every tool, for modding jobs). Ctrl+C stops the AI and leaves the
game running. Logs, the AI's notes and its transcript go to `build/ai-play/`. `--no-ai` only hosts and
prints the token, for connecting an AI yourself; `--help` lists the rest. The steps below are what the
script does, for other setups.

## 1. Serve Grok 2

Grok 2's weights are about 500 GB. xAI's recipe serves them with SGLang across 8 GPUs with more than
40 GB of memory each (tensor parallelism 8, FP8). Check the model card at
<https://huggingface.co/xai-org/grok-2> for the current requirements and the Grok 2 Community License,
which you accept by downloading.

```sh
pip install "sglang[all]>=0.5.1"
pip install -U "huggingface_hub[cli]"
hf download xai-org/grok-2 --local-dir /local/grok-2

python3 -m sglang.launch_server \
  --model /local/grok-2 \
  --tokenizer-path /local/grok-2/tokenizer.tok.json \
  --tp 8 --quantization fp8 --attention-backend triton \
  --host 127.0.0.1 --port 30000
```

When it prints that the server is ready, check it answers:

```sh
curl http://127.0.0.1:30000/v1/models
```

The served model name is the one `/v1/models` lists; pass it to the bridge with `--model`.

Not enough GPUs? Run a smaller open model that fits your hardware the same way (llama.cpp's
`llama-server`, Ollama, vLLM or LM Studio all serve the same API) and use its URL and model name below.
Models trained for tool calling do noticeably better at this game than models that are not.

## 2. Start the game with the AI endpoint

On the machine that hosts the world:

```sh
# From a checkout (needs the .NET 10 SDK); install-godot.sh downloads the pinned Godot once.
dotnet build Mechanica.sln
GODOT=$(tools/install-godot.sh)
$GODOT --path game -- --host --ai --ai-token s3cret
# or, from an exported build (tools/package.sh linux, or a release zip):
./Mechanica.x86_64 -- --host --ai --ai-token s3cret   # Mechanica.exe on Windows
```

The endpoint listens on `ws://127.0.0.1:27461`. A token is optional on 127.0.0.1 but keeps other local
programs out; it is required to listen on another address (`--ai-bind`). With only `--ai-token` the AI
gets all three scopes (observe, play and admin). To keep admin tools (spawning, clearing creations,
giving items) away from it, use `--ai-token` for play and add a separate `--ai-admin-token` you keep
yourself. [ai-endpoint.md](../ai-endpoint.md) has every flag. Friends can join the world as usual; the
AI shows up to them as a player named "Grok (AI)".

## 3. Run the bridge

From a checkout of the repository:

```sh
node tools/ai/bridge/openai-bridge.mjs \
  --base-url http://127.0.0.1:30000/v1 --model grok-2 \
  --tool-mode prompt \
  --token s3cret --name Grok \
  --notes grok-notes.md --transcript grok-session.jsonl
```

What happens:

1. The bridge connects to the game, authenticates and joins as the player "Grok (AI)".
2. It sends Grok the system prompt from [system-prompt.md](system-prompt.md) and the game's methods.
3. Grok calls tools; the bridge runs them on the game and sends back the answers and any game events
   (damage, dialogue, quest updates, chat).
4. It stops when Grok says `DONE` with no tool call, after `--max-steps` turns (200 by default), or when
   you press Ctrl+C. Grok's notes, recommendations and bug reports are in `grok-notes.md`; every turn
   is in `grok-session.jsonl`.

### Watching it play

The AI is a player in your world, so you see it walk, build and fight from your own seat in the game. Press
**V** (Watch players) to follow it with the camera: a banner at the top shows its name and what it last said
it is doing (the bridge passes the model's words to the game with `SetThought`; `--no-thoughts` turns that
off). Left and right click switch between players, X and C zoom between its own view and behind it, and V
or Esc gives you your view back. A short version of its thought also shows under its name tag. Its full
reasoning and every call it makes scroll by in the bridge's terminal.

### Tool mode

SGLang has no tool-call parser for Grok 2, so tell the bridge to describe the tools in the prompt
(`--tool-mode prompt`). Grok then calls a tool by writing a block like this, which the bridge runs:

````
```tool
{"name": "GetPlayer", "arguments": {}}
```
````

With servers and models that do support native tool calls (vLLM or SGLang with a parser, llama.cpp
with `--jinja`, Ollama, xAI's API), leave out `--tool-mode`: the default `auto` uses native tools and
switches to the prompt by itself if the server refuses them. If the model's chat template has no
system role, add `--no-system-role`.

### Giving it a job

The default job, from the system prompt, is to explore as a new player, do the first quests and report
what it finds. Give it another with `--job`:

```sh
--job "Build a small four-wheeled car from the vanilla parts, drive it, and report anything odd about the physics."
--job "Read the vanilla mod's part scripts, then write a mod 'grok_test' with one new part, load it, and test it."
--job "Look for bugs in crafting and the inventory. Write a bug report for each one with exact steps."
```

`--prompt my-prompt.txt` replaces the whole system prompt.

### Saving context

The game offers about a hundred methods. In prompt mode each one is a line of the prompt, a few
thousand tokens in all. To keep only what the job needs, pass `--groups` with area names from
[reference.md](reference.md), for example `--groups perception,remote,building` (`remote` holds `SetPlayerInput`; the `mechanica.*` methods are always offered). The bridge keeps the
newest 60 messages (`--history`) and clips long answers, telling the model to ask for less.

## Options

Run `node tools/ai/bridge/openai-bridge.mjs --help` for the full list:

| Option | Default | What it does |
| --- | --- | --- |
| `--base-url` | (required) | The model server's API root, ending in `/v1` |
| `--model` | (required) | The model name the server expects |
| `--api-key` | `OPENAI_API_KEY` or `XAI_API_KEY` | Bearer key, if the server wants one |
| `--tool-mode` | `auto` | `native`, `prompt` or `auto` |
| `--no-system-role` | off | Put the instructions in the first user message |
| `--temperature`, `--max-tokens` | 0.3, 2048 | Sampling |
| `--game` | `ws://127.0.0.1:27461` | The game's endpoint |
| `--token` | `MECHANICA_AI_TOKEN` | The endpoint token |
| `--name` | `Grok` | The player name ("(AI)" is added); `--no-join` stays an external agent |
| `--no-thoughts` | off | Don't show the model's words to people watching in the game |
| `--groups` | all | Only offer methods of these areas |
| `--topics` | hurt, vitals, dialogue, quest, chat, messages, raid | Event topics to subscribe to |
| `--job`, `--prompt` | | The job, or a whole prompt of your own |
| `--max-steps` | 200 | Model turns before stopping |
| `--stop-word` | `DONE` | What the model says when finished |
| `--notes` | `ai-notes.md` | Where notes and bug reports go |
| `--transcript` | none | Every turn as JSON lines |
| `--history` | 60 | Messages kept besides the instructions |

## A local model on Ollama

Ollama serves the same API with native tool calls, so no `--tool-mode` is needed. Pick a model trained
for tool calling that fits your GPU memory, for example `qwen3:32b` or `qwen3-coder:30b` with 24 GB or
more, `gpt-oss:20b` or `qwen3:14b` with about 16 GB, and `qwen3:8b` with 8 to 12 GB.

The tool list is long, so raise Ollama's context length to at least 32768 tokens. If Ollama runs on
another machine, make it listen on the network and open its port (Fedora shown):

```sh
sudo systemctl edit ollama   # add under [Service]:
# Environment="OLLAMA_HOST=0.0.0.0"
# Environment="OLLAMA_CONTEXT_LENGTH=32768"
sudo systemctl restart ollama
sudo firewall-cmd --permanent --add-port=11434/tcp && sudo firewall-cmd --reload
ollama pull qwen3:32b
```

Then, on the machine with the game:

```sh
node tools/ai/bridge/openai-bridge.mjs --base-url http://<ollama-host>:11434/v1 --model qwen3:32b \
  --token s3cret --name Qwen --notes ai-notes.md
```

## Grok through xAI's API

xAI's hosted API is OpenAI-compatible and supports native tool calls, so no local GPUs are needed:

```sh
export XAI_API_KEY=...   # from https://console.x.ai
node tools/ai/bridge/openai-bridge.mjs --base-url https://api.x.ai/v1 --model <model name from the console> \
  --token s3cret --name Grok
```

## When something goes wrong

- **"cannot connect to the game"**: the game is not hosting with `--ai`, or it uses another port; pass
  `--game ws://127.0.0.1:PORT`.
- **A token error**: pass the same token the game was started with (`--token` or `MECHANICA_AI_TOKEN`).
- **The AI's player appears but does nothing**: the model is still reading its first prompt. The bridge
  says so and prints how long the reply took. Offer fewer tools with `--groups` (for playing,
  `--groups perception,remote,items,survival,quests,chat` is about a quarter of the list), and on Ollama
  check with `ollama ps` that the context is as large as you set it; a context that is too small cuts
  the prompt short.
- **"model server answered 4xx/5xx"**: check `--base-url` (it ends in `/v1`) and `--model` against
  `/v1/models`.
- **The model writes about tools instead of calling them**: use `--tool-mode prompt`, lower the
  temperature, or try a model trained for tool calling.
- **It stops after three replies without a tool call**: the bridge stops a model that only talks.
  Give it a clearer `--job`.

The bridges are tested end to end against a real host and client by the headless test
`game/tests/aibridge`, with a scripted model in both tool modes.
