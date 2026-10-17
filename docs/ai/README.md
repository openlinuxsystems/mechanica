# Mechanica for AI agents

These pages are written to be fed to an AI (Grok, Claude or any model with tool calling) so it can play
Mechanica, mod it, take notes, make recommendations and find bugs. People connecting an AI start with
the connection guides; the AI itself reads the rest.

## What to feed the AI, in order

1. [system-prompt.md](system-prompt.md): a ready-to-use system prompt. Paste it as the AI's
   instructions; it tells the AI what the game is, how the endpoint works and how to behave.
2. [reference.md](reference.md): every method the game offers, grouped by area, with its params.
   It is generated from the game itself, so it is always current. The same data as JSON Schemas is in
   [describe.json](describe.json); the bridges turn it into the AI's tool list automatically, so a
   model with tool calling does not need the Markdown copy.
3. One guide for the job at hand:
   - [playing.md](playing.md): joining as a player, seeing the world, moving, building, surviving and
     the story.
   - [modding.md](modding.md): reading and changing mods and part scripts, and sharing them as packages.
   - [bug-hunting.md](bug-hunting.md): exploring on purpose, writing notes and recommendations, and
     reporting bugs with what is needed to reproduce them.

[`llms.txt`](../../llms.txt) at the repository root lists the same files for tools that look for it.

## Connecting an AI

The endpoint is JSON-RPC 2.0 over a WebSocket on the host ([ai-endpoint.md](../ai-endpoint.md) covers
starting it, tokens and scopes), so any program that can open a WebSocket can drive the game. Two
ready-made bridges in `tools/ai/bridge` (Node 22+, no packages) connect a model to it, hand it the
system prompt and the game's methods as tools, and save its notes and bug reports:

- [connect-grok-local.md](connect-grok-local.md): `tools/ai/play.sh` builds, hosts and connects in
  one command. The guide also covers Grok 2 served on your own GPUs with SGLang, through
  `openai-bridge.mjs`. The same bridge works with any OpenAI-compatible server (vLLM, llama.cpp,
  Ollama, LM Studio) and with xAI's hosted API, with native tool calls or tools described in the prompt.
- [connect-claude.md](connect-claude.md): Claude Code or Claude Desktop, through the MCP server
  `mcp-server.mjs`.

## Keeping these pages current

`reference.md` and `describe.json` are rebuilt from the game's `mechanica.describe` by
`tools/ai/gen-docs.sh`. The headless test `game/tests/aidocs` fails when they no longer match the game,
so anyone who adds, renames or re-documents an action or endpoint method regenerates them in the same
change. The guides are written by hand and name only methods the reference lists.
