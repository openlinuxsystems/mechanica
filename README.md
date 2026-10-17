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

## Releasing

`tools/package.sh linux` (or `windows`) exports a playable build into `build/Mechanica-<version>-<platform>.zip`:
the game, the bundled .NET runtime, `mods/` (all content) and `docs/` next to the executable, plus a player
`README.txt` (from `tools/release/`). It downloads the pinned export templates on first use. Check an
exported build with `./Mechanica.x86_64 --headless -- --self-test`: it loads a world and every part model and
sound clip, prints a summary and exits 0 only when nothing is missing.

To publish, push a tag: `git tag v0.2.0 && git push origin v0.2.0`. The Release workflow
(`.github/workflows/release.yml`) packages Windows and Linux, self-tests each on its own OS, and attaches
both zips to a GitHub Release for that tag. **Actions → Release → Run workflow** does the same from any
branch (with a tag to publish, or empty to only build the zips as run artifacts).

## Playing

The main menu starts a **New game** (a name, Survival or Creative, an optional seed, and whether to
open it to the LAN), loads a saved one, or joins one. Click to capture the mouse, then: WASD to move,
mouse to look, Space to jump, Shift to sprint, V to toggle the third-person camera, F to interact, J for
the quest log, Esc for the menu and F1 for help. [docs/playing.md](docs/playing.md) has every key.
Movement tuning (speeds, jump, capsule size) is data in `game/data/player.json`.

## Multiplayer

Every game is a session (D10): playing alone is hosting with nobody else connected. The host
simulates everything; clients send their input and predict their own player, and the host corrects
them when they disagree.

In a world, Esc opens the menu: **Open to LAN** hosts the session on a UDP port (default 27460) with
the world and players as they are. A host also answers LAN discovery queries on UDP port 27462, so the
main menu's **Join game** lists every game on the network (world, host, players, mode); it also joins
`address:port` typed in. From the command line (arguments after `--`):

```sh
godot --path game -- --host 27460 --max-players 4
godot --path game -- --join 192.168.1.20:27460
```

A joining client must run the same protocol version and the same mods (same versions, same load
order) as the host; otherwise the host refuses it and says which mods differ.

## World

The world is a 4 km island generated from a seed and the mods' world data (biomes, terrain, trees
and rocks, POIs); see [docs/world-generation.md](docs/world-generation.md). Each new game picks a
random seed; `--seed 1234` picks one. Clients regenerate the host's world from its seed, and the host
sends them every destroyed tree or rock.

## Saving and loading

The host saves the world: press Esc, then **Save or load…**, name the world and press **Save**. The main
menu's **Load game** (or `-- --load path/to/world.json`, combinable with `--host`) starts a saved world.
Saves are JSON files in the user data folder's `saves/` (one per world name); see
[docs/saves.md](docs/saves.md) for what they hold.

Each install has a player profile (`profile.json` in the user data folder: a random id and a name,
`--name Ada` renames it). The host keeps every player's inventory, health, respawn bed and position by
profile id, so a player who leaves and joins again, in this session or after a load, gets their things
back. Two instances with the same profile can't be in one session.

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

## Sound

Every sound is a sound event in a mod's `sounds/*.json`: clips, bus, loop, random pitch and volume,
distance fade and loop parameters. Parts bind their sound names to events, and the Part Editor's
Sounds tab edits both. The host decides world sounds and sends them to players in earshot; loops
follow their part. Vanilla's audio is original placeholder synthesis from
`tools/sounds/make_placeholders.py`. See [docs/sound-events.md](docs/sound-events.md).
