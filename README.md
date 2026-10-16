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
tools/smoke.sh            # builds, then runs the headless Godot smoke scene
```

`tools/smoke.sh` downloads the pinned Godot build into `.tools/` unless `GODOT` points
at an existing binary. Open `game/project.godot` in Godot 4.7.2 .NET to edit scenes.
