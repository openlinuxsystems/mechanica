# Mechanica docs

These docs ship with the game: the same files are in the repository's `docs/` folder and in the game's
Handbook (**H** or **F1**).

## Playing

- [How to Play](playing.md): controls, the HUD, tools, the inventory, options, playing together and saving.
- [Survival](survival.md): difficulty, friendly fire, health and respawning, farming and fire.
- [Saves](saves.md): what a saved world holds.
- [Credits](credits.md): who made the game.

## Making mods

Everything in the game is a mod, vanilla included, and every mod tool is built into the game under **Mod
tools** on the main menu. Read these in order:

1. [Getting Started](getting-started.md): make a mod and your first part in the Part Editor, give it a recipe,
   test it and play it.
2. [Your First Mod](first-mod.md): a whole mod end to end: a part and its recipe, an item with a script, a mod
   script, a translation, a rules patch, testing with a client, and a package.
3. [Mod Tools](mod-tools.md): every tool on the menu: Part Editor, Bot Behavior Editor, Recipe Editor, Sound
   Editor, Test Sandbox, Mod Manager, batches, the Lua script editor and the Handbook.
4. [Part Data Schema Reference](part-data-schema.md): every key of parts, items and recipes.
5. [Lua API Reference](lua-api.md): every function and hook of part, item and mod scripts.

Then the topic guides:

- [Items Tutorial](items-tutorial.md): food, a gun with ammo and projectiles, and a thrown item.
- [Mod Scripts Cookbook](mod-scripts.md): events, timers, actions and shared code for the whole world.
- [Socket and Joint Guide](socket-joint-guide.md): bodies, sockets, hinges, sliders and springs.
- [Logic and Power Tutorial](logic-power-tutorial.md): links, signals, seats and settings.
- [Aero and Propulsion Tutorial](aero-tutorial.md): wings, thrusters and rotors.
- [IO Ports](io-ports.md): the standard input and output face machines carry for tubes.
- [Bot Tutorial](bot-tutorial.md): bots, behaviour trees, bot scripts and raids.
- [Quests and Dialogue](quests.md): quests, objectives, dialogue and story batches.
- [World Generation and POI Prefabs](world-generation.md): biomes, terrain, roads and POIs.
- [Player Characters](characters.md): adding a character or a skin.
- [Sound Events](sound-events.md): sound banks and part sounds.
- [Translating](translating.md): language files for the game and for mods.
- [Packaging Guide](packaging-guide.md): `mod.json`, versions, dependencies, overrides, patches, game rules,
  load order and packages.
- [UI Style Guide](ui-style-guide.md): the palette, theme and rules for new menus and mod tools.

Example mods, in `examples/mods/` of the repository (copy one into your `mods/` folder to try it):

| Example | Shows | Guide |
|---------|-------|-------|
| `example_bearing` | A motor bearing driven by its logic input | [Aero and Propulsion Tutorial](aero-tutorial.md) |
| `example_thruster` | A thruster that pushes its creation | [Aero and Propulsion Tutorial](aero-tutorial.md) |
| `example_bots` | A patrolling sentry drone with a script | [Bot Tutorial](bot-tutorial.md) |
| `example_items` | Food, a gun with ammo, a thrown item, and their recipes | [Items Tutorial](items-tutorial.md) |
| `example_modscript` | A mod script with events, a timer and a library, a French translation and a rules patch | [Mod Scripts Cookbook](mod-scripts.md) |

## AI and tooling

- [External AI Endpoint and Record/Replay](ai-endpoint.md): the WebSocket JSON-RPC endpoint that lets outside
  programs play and build through the Action API, and recording and replaying a session.
- [AI Notes, Recommendations and Bug Reports](ai-reports.md): what agents file, and the AI Reports window.
- The generated reference of every action and endpoint method, and the agent guides, are in `docs/ai/` of the
  repository (start with its `README.md`).

## Engine settings and game rules

`game/data/*.json` (`player.json`, `streaming.json`, `building.json`, `survival.json`, ...) tunes the
engine itself: movement, streaming radii, build reach, damage rules, sync rates. These files ship with the
game and are not moddable, because every peer must run the same engine. The exception is the game rules: the
gameplay keys of `survival.json`, `fire.json` and `building.json` (health, respawn, starting items, fire spread,
paint, the sledgehammer, the Lift), which a mod patches with `rules/<name>.patch.json`. Host and clients check
they run the same rules when a client joins. See [Game rules](packaging-guide.md#game-rules). Content (parts,
items, recipes, bots, raids, worlds, POIs, quests, characters, sounds, translations) lives in mods.
