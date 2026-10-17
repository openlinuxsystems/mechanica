# Mechanica docs

The spec requires these docs to ship both here and as in-game help. Each one is
written by the chunk that builds the feature it covers.

| Doc | Written in |
|-----|------------|
| [Getting Started](getting-started.md) | Part Editor v1 |
| [Part Data Schema Reference](part-data-schema.md) | Part Editor v1 |
| [Lua API Reference](lua-api.md) | MoonSharp host |
| [Socket and Joint Guide](socket-joint-guide.md) | Joint primitives |
| [Logic and Power Tutorial](logic-power-tutorial.md) | Signal/logic bus |
| [Aero and Propulsion Tutorial](aero-tutorial.md) | Aero/propulsion |
| Example mods ([bearing](../examples/mods/example_bearing), [thruster](../examples/mods/example_thruster)) | Aero/propulsion |
| [Packaging Guide](packaging-guide.md) | Mod Package Manager |
| [Bot Tutorial](bot-tutorial.md) | Bot behaviour runtime |
| [Example bot mod](../examples/mods/example_bots) | Bot behaviour runtime |
| [External AI Endpoint and Record/Replay](ai-endpoint.md) | External AI endpoint |

Other references:

- [How to Play](playing.md): controls, playing together, saving
- [UI Style Guide](ui-style-guide.md): palette, theme and rules for new menus and mod tools
- [Translating](translating.md): language files, the pseudo-locale and how UI text reaches the string tables

- [World Generation and POI Prefabs](world-generation.md)
- [Sound Events](sound-events.md)
- [Survival](survival.md)
- [Saves](saves.md)
- [Quests and Dialogue](quests.md)

## Engine settings

`game/data/*.json` (`player.json`, `streaming.json`, `building.json`, `survival.json`, ...) tunes the
engine itself: movement, streaming radii, build reach, damage rules, sync rates. These files ship with
the game and are not moddable. Every peer must run the same engine, and a mod can't change them without
a load step to check them on join. Content (parts, items, recipes, bots, raids, worlds, POIs, quests)
lives in mods.
