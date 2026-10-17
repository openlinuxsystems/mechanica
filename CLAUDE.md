# Mechanica conventions

Mechanica is a Scrap Mechanic-style physics sandbox: Godot 4.7.2 .NET (C#), Jolt physics,
MoonSharp Lua, Godot MultiplayerAPI over ENet.

## Build and verify (every change)

```sh
dotnet build Mechanica.sln      # warnings are errors
dotnet test Mechanica.sln
tools/smoke.sh                  # headless Godot smoke scene, needed for anything in game/
```

CI (`.github/workflows/ci.yml`) runs the same three steps. Keep it green.
`tools/lua-bench.sh` runs the D2 Lua benchmark (headless, then in Godot with real step times).

## Toolchain pins

- .NET SDK: `global.json`. Every project targets `net10.0` (`Directory.Build.props`).
- Godot: `tools/versions.env` and the `Godot.NET.Sdk/x.y.z` attribute in `game/Mechanica.Game.csproj`.
  Bump both together.
- NuGet packages: versions live only in `Directory.Packages.props` (central package management).
  `PackageReference` items never carry a `Version`.

## Project boundaries

- `src/Mechanica.Core` must never reference Godot. Logic-shaped code (data, Creation graph,
  signal graph, crafting, raid math, Action API validation, serialization) goes here with xUnit
  tests in `tests/Mechanica.Core.Tests`.
- `src/Mechanica.Scripting` hosts MoonSharp. No Godot reference; tests in
  `tests/Mechanica.Scripting.Tests`. Scripts run sandboxed (no io/os/file loading).
- `game/` is the only Godot code. Keep it thin: it adapts Core to nodes, physics and UI.
- Headless Godot checks live in `game/tests/` as scenes that call `GetTree().Quit(code)`.

## Design rules (from the spec review decisions)

- Units are SI everywhere (kg, m, s, N, N·m). The build grid is 0.25 m (`MechanicaInfo.GridSize`).
- Physics: rigidly attached parts merge into one rigid Body with a compound shape. Real Jolt joints
  exist only at bearings (hinge), pistons (slider) and suspension (spring). A Creation is Bodies plus
  joints. Welding merges Bodies; it never creates a fixed joint.
- Part behaviour, aero included, lives in Lua. C# only exposes generic primitives (forces, torques,
  body state, joint motor targets, raycasts, signal bus, inventory, sound). No part-specific C#
  classes. Moving a hot path to C# needs Chase's approval.
- Multiplayer from the start: the host simulates and validates everything. Every state change goes
  through the Action API. Each Creation keeps an `authorityPeer` field. A feature is done only when
  it works with a host plus at least one client.
- Data is JSON only (System.Text.Json; comments and trailing commas allowed on read; pretty-printed,
  stable key order on write). No YAML.
- Content is a mod: `mods/<mod_id>/` holds `mod.json`, `parts/*.json`, `scripts/*.lua`,
  `meshes/*.glb|.gltf`, `textures/*.png`, `sounds/*.ogg|.wav` with `sounds/**/*.json` (sound events; see
  `docs/sound-events.md`), `world/worldgen.json` (world
  generation rules; the last mod in load order wins) and `pois/*.json` (POI prefabs; see
  `docs/world-generation.md`) and `recipes/*.json` (crafting recipes; see `docs/part-data-schema.md`), `bots/*.json` (bot types:
  stats, attacks, drops, behaviour tree, optional Lua script) and `raids/raids.json` (the raid table; the
  last mod in load order wins) and `quests/*.json` (quests and dialogue; see `docs/quests.md`), plus `overrides/<other_mod>/` (full part files that
  replace another mod's part, and `overrides/<other_mod>/recipes/**/*.json` for its recipes; the mod later in
  load order wins). Paths in data are relative to the mod
  root (`other_mod:path` for another mod). No `res://` paths or Godot-imported formats in mod data.
  Reject `..` and absolute paths.
- Mod tools are in-game modes and ship in exported builds; no Godot editor plugins.
- Once the Part Editor exists, vanilla items are authored through it, not by hand-written JSON.
  Families of similar parts (blocks and wedges) are Part Editor batches in
  `mods/vanilla/editor/batches/`; edit the batch and re-run `tools/author-vanilla.sh`. Bots and the
  raid table are the Bot Behavior Editor batch `mods/vanilla/editor/batches/bots.json` the same way, and
  the story (quest POIs and quests) is `mods/vanilla/editor/batches/story.json`.
- Item and sound IDs come from the canonical catalog (CATALOG_IDS.md in the project files). Use the
  original names (Scuttler, Fabricator, …), never Scrap Mechanic's.
- Balance numbers live in data, never in code.
- UI follows the UI spec (`UI_SPEC.md` in the project files; it wins over SPEC_SUMMARY on controls, UI and
  menus). Every screen inherits the code-built theme in `game/scripts/Ui/MechanicaTheme.cs`: no per-screen
  colours, and never orange. Use `UiTokens` for hand-drawn colours, `ToolWindow` for a titled panel with an X
  and Esc to close, and `ConfirmDialog` before anything destructive. Any other panel the player opens over
  the world calls `UiInput.KeepMouse` so clicks and wheel turns over it never reach the game; cover new
  windows in `game/tests/windows`.
- UI text goes through the string tables (`game/scripts/Ui/Loc.cs`, `docs/translating.md`): literal text on a control
  auto-translates, text built at run time uses `Loc.T("… {0}", value)`, and player or mod text uses `Loc.Verbatim`.
  After changing UI text, re-run `res://tests/language/LanguageTest.tscn -- --collect` to regenerate `game/data/lang/en.json`.
- Never hard-code a key. Add the action to `src/Mechanica.Core/Input/InputActions.cs` (spec §1 default) and
  match it with `GameInput.Pressed`; HUD hints use `GameInput.Label` so they follow the player's bindings.
- Engine settings (`game/data/*.json`: movement, streaming, build reach, sync) ship with the build and
  are not moddable. Content goes in mods (decided in chunk 24).

## Code style

- File-scoped namespaces, nullable enabled, `_camelCase` private fields, XML doc comments on public
  types.
- One PR per work chunk. Add tests for anything logic-shaped.
