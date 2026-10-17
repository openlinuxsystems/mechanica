# Mechanica

A physics sandbox survival game about building machines, built with Godot 4.7 .NET (C#),
Jolt physics and MoonSharp Lua.

## Layout

| Path | What lives there |
|------|------------------|
| `src/Mechanica.Core` | Pure C# game logic (no Godot): data, Creation graph, signals, crafting, Action API |
| `src/Mechanica.Scripting` | MoonSharp Lua host and API bindings (no Godot) |
| `tests/` | xUnit tests for the libraries above |
| `game/` | The Godot .NET project (rendering, Jolt bodies, input, tools, UI, networking) |
| `mods/vanilla/` | All game content, packaged as a mod exactly like third-party mods |
| `docs/` | Player and modder documentation |
| `tools/` | Pinned versions and build scripts |

## Building

Requirements: .NET SDK 10 (pinned in `global.json`) and Godot 4.7.2 .NET (pinned in
`tools/versions.env`).

```sh
dotnet build Mechanica.sln
dotnet test Mechanica.sln
tools/smoke.sh            # builds, then runs every headless Godot test scene in game/tests/
```

`tools/smoke.sh` downloads the pinned Godot build into `.tools/` unless `GODOT` points
at an existing binary. Open `game/project.godot` in Godot 4.7.2 .NET to edit scenes.

## Playing the test world

The main scene is a flat test world. Click to capture the mouse, then: WASD to move, mouse to
look, Space to jump, Shift to sprint, V to toggle the third-person camera, Esc to free the mouse.
Movement tuning (speeds, jump, capsule size) is data in `game/data/player.json`.

## Multiplayer

Every game is a session (D10): playing alone is hosting with nobody else connected. The host
simulates everything; clients send their input and predict their own player, and the host corrects
them when they disagree.

Press Esc to free the mouse and open the connect panel: **Host** opens the session on a UDP port
(default 27460), **Join** connects to `address:port`. From the command line (arguments after `--`):

```sh
godot --path game -- --host 27460 --max-players 4
godot --path game -- --join 192.168.1.20:27460
```

A joining client must run the same protocol version and the same mods (same versions, same load
order) as the host; otherwise the host refuses it and says which mods differ.

All state changes go through the Action API (`Main.Actions.Request("Respawn", new NoArgs())`): on the
host the call runs directly, on a client it is sent to the host, which validates it as that peer and
answers with a result (`ok`, or a code such as `unknown_action`, `bad_args`, `rejected`).

### Multiplayer tests

`game/tests/harness/NetTestHarness.cs` runs a host and any number of clients in one headless
process, each in its own viewport with its own physics world and multiplayer API, talking over
real ENet sockets on localhost. `game/tests/net/NetTest.tscn` uses it to check joining, movement
sync, prediction, the Action API, bad packets, late join, full sessions and leaving. Later chunks
add their own host-plus-client scenes the same way (subclass `StagedTest`, add a `NetTestHarness`).

### External AI endpoint

`--ai [port]` on the host opens a WebSocket JSON-RPC endpoint (loopback, port 27461 by default) so
outside programs can call the Action API like a player, and `--record file` / `--replay file`
record and replay the host's Action API calls. See [docs/ai-endpoint.md](docs/ai-endpoint.md) and the
example agent `tools/ai/drive_car.mjs`.

