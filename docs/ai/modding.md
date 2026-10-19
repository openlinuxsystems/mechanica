# Modding Mechanica as an AI

All of Mechanica's content is mods, vanilla included. A mod is a folder `mods/<mod_id>/` of JSON data,
Lua scripts and assets; there is no compiled code. Part behaviour (engines, wheels, aerodynamics, farming,
weapons) lives in Lua, and C# only offers generic primitives (forces, torques, joint motors, raycasts,
signals, inventories, sound). So almost any change to how the game plays is a data or script change.

Read these human docs for the formats; they are the source of truth:

| Topic | Doc |
|-------|-----|
| Folder layout, `mod.json`, versions, dependencies, overrides, load order, packages | [packaging-guide.md](../packaging-guide.md) |
| Part files: size, collision, sockets, joints, inventory, recipes, survival, logic, settings, menus | [part-data-schema.md](../part-data-schema.md) |
| Lua scripts for parts and items: hooks, globals, `part`, `creation`, `world`, `player`, `item` | [lua-api.md](../lua-api.md) |
| Bots and raids, quests, characters, world generation, sound events | [bot-tutorial.md](../bot-tutorial.md), [quests.md](../quests.md), [characters.md](../characters.md), [world-generation.md](../world-generation.md), [sound-events.md](../sound-events.md) |

Rules every mod follows: units are SI; data is JSON (comments and trailing commas are fine);
paths in data are relative to the mod root (`other_mod:path` for another mod's file), never absolute and
never with `..`; balance numbers live in data. Game-wide ids are `mod_id:id`. A bare id means different
things in two places: in Action API arguments (everything you send, including `actions.call` from a mod script)
it means vanilla (`wheel` is `vanilla:wheel`), but in a mod's own data files it means that file's mod (a part in
`my_mod` naming `seat` means `my_mod:seat`; sound ids fall back to vanilla's). Write another mod's ids in full.
The whole rule is in [Ids and namespacing](../part-data-schema.md#ids-and-namespacing).

## Reading what is loaded

These work for any scope and change nothing:

- `ListParts`, `ListItems`, `GetPart` (the part file exactly as loaded, plus what makes it and what it
  makes), `ListRecipes`, `ListBotTypes`, `GetQuestBook`, `ListCharacters`.
- `GetCreation` on a machine shows each part's state, settings, health and damage, which is how you
  check what a script did.
- Subscribe to `messages`: every Lua `print`, warning and error from part, item and bot scripts arrives
  there with the script, Creation and part it came from. A script error is the first thing to look for
  when a part does nothing.

## Trying a change in the world

With the `admin` scope, `SpawnPart` puts a part in the world without paying for it and
`ClearCreations` wipes every machine; use them in a test world, never in someone's save. As a player,
build the machine with `PlacePart` and drive it with seats, controllers and part menus, the same way a
human tests a mod.

## Sharing mods as packages

The `package.*` methods (admin scope) are the Mod Manager's Export, Install and Uninstall:

| Step | Method |
|------|--------|
| See packages in the packages folder | `package.list` |
| Check one before installing (valid, change kind, problems) | `package.inspect {"file": ...}` |
| Export an installed mod to a verified package | `package.export {"id": ...}` |
| Install, or replace an installed copy | `package.install {"file": ..., "replace": false}` |
| Uninstall (lists mods that depend on it) | `package.uninstall {"id": ...}` |
| Check an installed mod still matches its package | `package.verify {"id": ...}` |

Installs and uninstalls take effect with the next world and are announced on the `mods` topic. Every
problem has a `code` from the Packaging Guide's diagnostics table; fix the cause it names rather than
retrying.

## Writing mod files

Editing mod files and scripts over the endpoint comes with the mod tools methods. When
`mechanica.describe` lists them (they appear in [reference.md](reference.md) once they ship), use them
instead of asking a person to edit files: they validate data with the same loader the game uses and
report problems by file and JSON path. Until then, write out the exact file contents you propose
(full JSON or Lua, with its path under `mods/<mod_id>/`) in your notes so a person can apply them.

## Good modding habits

- Make a new mod for your changes instead of editing vanilla; replace a vanilla part with
  `overrides/vanilla/parts/<file>.json` in your mod.
- Change numbers in data, not code. If a behaviour needs a new C# primitive, that is a recommendation
  for the developers, not something to work around.
- After a change, check `messages` for script errors and look at the result with `GetCreation` or
  `GetPart` before calling it done.
