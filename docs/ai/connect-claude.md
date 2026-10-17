# Connecting Claude

Claude connects through MCP (the Model Context Protocol). `tools/ai/bridge/mcp-server.mjs` is a small
MCP server that gives Claude the game's methods as tools, the game's events, a notes tool, and these AI
docs as resources. It works with Claude Code, Claude Desktop and any other MCP client. It needs Node 22
or newer and no packages.

```
Claude (Code or Desktop)  <-stdio->  tools/ai/bridge/mcp-server.mjs  <-WebSocket->  game host (--ai endpoint)
```

## 1. Start the game with the AI endpoint

```sh
# From a checkout (needs the .NET 10 SDK); install-godot.sh downloads the pinned Godot once.
dotnet build Mechanica.sln
GODOT=$(tools/install-godot.sh)
$GODOT --path game -- --host --ai --ai-token s3cret
# or, from an exported build (tools/package.sh linux, or a release zip):
./Mechanica.x86_64 -- --host --ai --ai-token s3cret   # Mechanica.exe on Windows
```

The endpoint listens on `ws://127.0.0.1:27461`. See [connect-grok-local.md](connect-grok-local.md#2-start-the-game-with-the-ai-endpoint)
for tokens and scopes; [ai-endpoint.md](../ai-endpoint.md) has every flag.

## 2. Add the server to Claude

### Claude Code

From the repository root:

```sh
claude mcp add mechanica --env MECHANICA_AI_TOKEN=s3cret -- node tools/ai/bridge/mcp-server.mjs --name Claude --notes claude-notes.md
```

Use an absolute path to `mcp-server.mjs` if you add it from elsewhere, or add `--scope project` to
share it through the repository's `.mcp.json`. Start `claude`, check `/mcp` lists `mechanica` as
connected, and give it a job:

```
Play Mechanica: explore as a new player, do the first quests, and write a bug report with
bridge_note for anything that behaves oddly.
```

### Claude Desktop

Add the server to `claude_desktop_config.json` (Settings > Developer > Edit Config), with the absolute
path to your checkout, then restart Claude Desktop:

```json
{
  "mcpServers": {
    "mechanica": {
      "command": "node",
      "args": ["/path/to/mechanica/tools/ai/bridge/mcp-server.mjs", "--name", "Claude", "--notes", "/path/to/claude-notes.md"],
      "env": { "MECHANICA_AI_TOKEN": "s3cret" }
    }
  }
}
```

## What Claude gets

- **Tools**: every method the token's scopes allow, named as in [reference.md](reference.md) with dots
  turned into `__` (`mechanica.join` is `mechanica__join`), plus:
  - `game_events`: the events pushed since the last call (damage, health, dialogue, quests, chat,
    script messages, raids).
  - `game_wait`: waits some seconds of real time and returns the events meanwhile, for walking,
    machines and crops.
  - `bridge_note`: saves a note, recommendation or bug report to the `--notes` file.
- **Instructions**: the system prompt from [system-prompt.md](system-prompt.md), sent when the client
  connects.
- **Resources**: these docs as `mechanica://docs/ai/<file>`, so Claude can read the guides and the
  reference when it needs them.

With `--name Claude` the server joins the world as the player "Claude (AI)" as soon as it reaches the
game; without it, Claude can call `mechanica__join` itself or stay an external agent. The server starts
before the game is needed and reconnects if the game restarts, so Claude can be opened first.

## Watching it play

Claude is a player in your world, so you see it from your own seat in the game. Press **V** (Watch players)
to follow it with the camera; X and C zoom, left and right click switch players, and V or Esc stops. When
Claude calls `SetThought`, the banner shows what it is doing (the system prompt asks it to).

## Options

| Option | Default | What it does |
| --- | --- | --- |
| `--game` | `ws://127.0.0.1:27461` | The game's endpoint |
| `--token` | `MECHANICA_AI_TOKEN` | The endpoint token |
| `--name` | none | Join as a player with this name ("(AI)" is added) |
| `--topics` | hurt, vitals, dialogue, quest, chat, messages, raid | Event topics to subscribe to |
| `--groups` | all | Only offer methods of these areas, to save context |
| `--notes` | `ai-notes.md` | Where `bridge_note` saves notes and reports |

## Claude through the API

To run Claude unattended against the API instead of through an app, use Claude Code headless
(`claude -p "..." --allowedTools "mcp__mechanica"`) with the server added as above, or connect the
MCP server from your own program with the Claude Agent SDK.

## When something goes wrong

- **The tools fail with "cannot connect to the game"**: start the game with `--ai`, or pass
  `--game ws://127.0.0.1:PORT` in the server's arguments.
- **A token error**: the server's `MECHANICA_AI_TOKEN` (or `--token`) must match the game's.
- **Claude Desktop does not show the server**: the path in `args` must be absolute, and `node` must be
  on the PATH Claude Desktop sees (or give the absolute path to node as `command`). Its MCP logs are
  in Settings > Developer.
- **Messages from the server**: it logs to stderr with the prefix `[mechanica-mcp]`, which Claude Code
  shows with `claude --debug` and Claude Desktop keeps in its MCP log.
