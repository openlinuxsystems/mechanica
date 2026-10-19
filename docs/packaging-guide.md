# Packaging Guide

How to lay out, version, package and share a Mechanica mod, and how players manage the mods they
install. Vanilla content is packaged exactly the same way: open `mods/vanilla/` next to the game
executable to see a complete example.

## 1. Folder layout

A mod is a plain folder inside `mods/`. The folder name is the mod id.

```
mods/
  better_wheels/
    mod.json            required: identity, version, dependencies, conflicts
    parts/              part and item definitions (*.json, any subfolders)
      wheels/sport_wheel.json
    overrides/          changes to other mods' content, one folder per mod id: part files, recipes/, bots/,
                        quests/, pois/, characters/, *.patch.json merge patches and remove.json
      vanilla/wheel.json
    scripts/            Lua behaviour (*.lua)
    meshes/             glTF models (*.glb, *.gltf)
    textures/           icons and textures (*.png)
    sounds/             audio (*.ogg, *.wav)
    sounds/**/*.json    sound events, see sound-events.md
    recipes/            crafting recipes (*.json), see part-data-schema.md
    world/worldgen.json world generation rules; the last mod in load order that ships one wins
    pois/               POI prefabs (*.json), see world-generation.md
    bots/               bot types (*.json), see bot-tutorial.md
    raids/raids.json    the raid table; the last mod in load order that ships one wins
    quests/             quests and dialogue (*.json), see quests.md
    characters/         player characters and skins (*.json), see characters.md
```

Rules the loader enforces:

- The mod id is lowercase snake_case (a letter, then letters, digits or `_`) and equals the folder name.
- Data files are JSON. Comments (`//`, `/* */`) and trailing commas are allowed.
- Asset paths in data are relative to the mod root: `"mesh": "meshes/sport_wheel.glb"`.
  To use a file from another mod, write `"other_mod:meshes/x.glb"` and list `other_mod` as a
  dependency. Absolute paths, `..`, backslashes, `res://` and URLs are rejected.
- Only the formats above are read. Do not ship Godot-imported files (`.import`, `.tres`, `.mesh`).

## 2. mod.json

```json
{
  "format_version": 1,
  "id": "better_wheels",
  "name": "Better Wheels",
  "version": "1.2.0",
  "description": "Sport and off-road wheels, and a grippier vanilla wheel.",
  "authors": ["Ada"],
  "dependencies": [
    { "id": "vanilla", "version": "^0.1.0" }
  ],
  "conflicts": [
    { "id": "old_wheel_pack" }
  ]
}
```

| Key | Required | Meaning |
|-----|----------|---------|
| `format_version` | no | Data format of the mod's files. Omit it or write `1`. |
| `id` | yes | Mod id, equal to the folder name. Every part's game-wide id is `id:part_id`. |
| `name` | yes | Name shown in the Mod Manager. |
| `version` | yes | Semantic version, `major.minor.patch`, optionally `-prerelease` (`1.0.0-beta.2`). |
| `description` | no | One paragraph shown in the Mod Manager. |
| `authors` | no | List of names. |
| `dependencies` | no | Mods that must be installed, enabled and loaded before this one. |
| `conflicts` | no | Mods this one cannot run with. |

Unknown keys are kept but produce a warning, with a suggestion when they look like a typo.

## 3. Versioning

Use [semantic versioning](https://semver.org):

- **Patch** (`1.2.0` → `1.2.1`): fixes that change no ids and break no Creations.
- **Minor** (`1.2.1` → `1.3.0`): new parts or new optional behaviour.
- **Major** (`1.3.0` → `2.0.0`): anything that can break a mod depending on yours or a saved Creation,
  such as removing or renaming a part id, changing a part's size or sockets, or removing an asset other
  mods reference.

Never reuse a part id for a different part. Saved Creations store part ids, so renaming one breaks
every save that uses it.

### Version ranges

`dependencies` and `conflicts` entries take an optional `version` range. Omitted means any version.
Terms separated by spaces must all hold.

| Range | Accepts |
|-------|---------|
| `*` | any version |
| `1.2.0` | exactly 1.2.0 |
| `>=1.2.0 <2.0.0` | 1.2.0 up to, not including, 2.0.0 |
| `^1.2.0` | 1.2.0 and later with the same major (for `^0.x.y`, the same minor) |
| `~1.2.0` | 1.2.0 and later with the same minor |

Depend on the lowest version you tested with, usually with `^`: `{ "id": "vanilla", "version": "^0.1.0" }`.

## 4. Dependencies

A dependency guarantees three things: the mod is installed and enabled, its version matches the
range, and it loads before yours. You must also list a mod as a dependency before you can reference its
assets or override its parts.

If a dependency is missing, disabled, the wrong version, broken, or part of a cycle, your mod does not
load and the Mod Manager says why. Mods that depend on yours do not load either; everything else does.

## 5. Conflicts

List a mod under `conflicts` when the two cannot work together, for example two mods that replace the
same system in incompatible ways. A version range limits it to some versions:

```json
"conflicts": [{ "id": "old_wheel_pack", "version": "<2.0.0" }]
```

When a conflicting mod is enabled, **your mod is the one left out** (error `MD0112`), and the Mod
Manager tells the player to disable one of the two. Prefer overrides or a dependency over a conflict
whenever your mod can coexist with the other one.

## 6. Overriding another mod's parts

To change a part another mod defines (including vanilla), put a complete part definition with the same
`id` in `overrides/<mod_id>/`. It replaces the original everywhere, and the part keeps its game-wide id
(`vanilla:wheel`), so saved Creations keep working.

```
mods/better_wheels/overrides/vanilla/wheel.json   replaces vanilla:wheel
```

- The target mod must be in your `dependencies` (error `MD0203` otherwise), and the part must exist in
  it (error `MD0204`).
- An override is a whole definition, validated like any part. In the Part Editor, open the original
  and press **Override in my mod…**: it copies the part into the right folder, points its assets back at
  the original mod and adds the dependency for you.
- Relative asset paths in an override resolve inside **your** mod. To keep the original mesh, write
  `"mesh": "vanilla:meshes/wheel.glb"`.
- When two mods override the same part, the one later in the load order wins and the Mod Manager shows
  a warning (`MD0205`). If your mod deliberately builds on another mod's override, depend on that mod:
  then yours always loads later and no warning is shown.

### Overriding recipes

A recipe is replaced the same way: put a complete recipe with the same `id` in
`overrides/<mod_id>/recipes/`. It keeps its game-wide id (`vanilla:chest`), so crafting queues and
saves keep working.

```
mods/cheap_chests/overrides/vanilla/recipes/chest.json   replaces the recipe vanilla:chest
```

- The same dependency rule applies (`MD0203`), and the recipe must exist in that mod (`MD0703`).
- It is checked like any recipe (`MD0701`). A recipe override with errors is left out and the original
  recipe stays.
- When two mods override the same recipe, the later one in the load order wins with a warning
  (`MD0704`), unless one depends on the other.

### Overriding bots, quests, POIs and characters

The same works for every other kind of content: put a complete file with the same id in the matching
folder of `overrides/<mod_id>/`.

```
mods/tough_bots/overrides/vanilla/bots/scuttler_red.json        replaces the bot vanilla:scuttler_red
mods/my_story/overrides/vanilla/quests/first_farm.json          replaces the quest vanilla:first_farm
mods/my_world/overrides/vanilla/pois/camp.json                  replaces the POI vanilla:camp
mods/my_look/overrides/vanilla/characters/scrapper_male.json    replaces the character vanilla:scrapper_male
```

A quest's id is its file name, as in `quests/`; the others use their `id`. Relative asset paths resolve
inside your mod, as for part overrides.

### Patching instead of replacing

A whole-file override stops picking up the original mod's fixes. To change only a few values, write a
**merge patch** instead: a file ending in `.patch.json`, named for the entry it changes (or with an `id`),
holding only the keys to change. It works in every override folder, parts included:

```
mods/tough_bots/overrides/vanilla/bots/scuttler_red.patch.json   { "health": 400 }
mods/grippy/overrides/vanilla/wheel.patch.json                    { "name": "Grippy Wheel", "visual": { "roughness": 1 } }
mods/fast_craft/overrides/vanilla/recipes/barrel.patch.json       { "time": 1 }
```

Objects merge key by key, `null` removes a key, and anything else (numbers, strings, lists) replaces the
original value ([RFC 7386](https://www.rfc-editor.org/rfc/rfc7386)). The result is checked like any
entry. Patches stack: a mod that depends on another mod's override or patch builds on top of it.

### Removing content

`overrides/<mod_id>/remove.json` takes entries out of the game, listed by id under `parts`, `recipes`,
`bots`, `quests`, `pois` and `characters`:

```json
{ "parts": ["antenna"], "recipes": ["barrel"], "bots": ["scuttler_blue"] }
```

A recipe that makes, uses or runs at a removed part is removed with it. Other content that still names
a removed entry (a quest reward, a raid, a POI site) reports an error, so remove or patch that too.
Saved Creations with a removed part fail to load that part.

When two mods change the same entry without one depending on the other, the later one in the load order
wins and the Mod Manager shows a warning (`MD1502`). Changing an entry that doesn't exist, or was removed
by an earlier mod, is an error (`MD1501`); removing one that isn't there is a warning.

## 7. Load order and the Mod Manager

Players manage mods in the in-game **Mod Manager** (under **Mod tools** on the main menu). It lists
every installed mod in load order and shows, for the setup on screen:

- a checkbox per mod. Enabling a mod also enables what it depends on. Disabling a mod also disables the
  mods that depend on it. Vanilla cannot be disabled.
- **Move up** and **Move down**. A mod always stays below the mods it depends on, so a move stops there.
  Mods with no dependency between them load in the order shown, which decides which override wins.
- each mod's status (loaded, disabled, not loaded and why, overrides won or lost) and details.
- every error, warning and conflict the setup would load with, before anything is saved.
- **Export…**, **Install…** and **Uninstall** for mod packages (§9).

**Apply** saves the setup to `mods.json` in the user data folder (`user://mods.json`; on Linux
`~/.local/share/godot/app_userdata/Mechanica/`). The game reads it the next time it starts or hosts.
The file looks like this:

```json
{
  "format_version": 1,
  "mods": [
    { "id": "vanilla", "enabled": true },
    { "id": "better_wheels", "enabled": true },
    { "id": "old_wheel_pack", "enabled": false }
  ]
}
```

Newly installed mods that are not in the file are enabled and load after the listed ones, by id. Entries
for uninstalled mods are kept, so a reinstalled mod gets its place back. Without the file every mod is
enabled. A broken file is reported and ignored.

## 8. Multiplayer

The host's content is the game's content. Every player who joins needs the **same mods, same
versions, enabled in the same order**. The game compares a fingerprint of every loaded manifest, part and
override on join, so any change to a loaded value counts (formatting and comments do not). Bump your
version for every release so players can tell which copy they have.

## 9. Packaging and sharing

Share a mod as a **mod package**: a zip named `<id>-<version>.zip`, made and installed by the Mod Manager.
The format is built for a future mod store, so the store, the game and the AI endpoint all check packages
with the same code (`Mechanica.Core.Packages.ModPackages`).

### Exporting

1. Open the Mod Manager, select your mod and press **Export…**, then pick a folder (it starts in
   `user://mod_packages`).
2. The game checks the mod the way it loads it, with its dependencies and every kind of content (parts,
   recipes, sounds, bots, raids, world generation, quests, characters). **Any error refuses the export**
   and the Packages panel lists the problems. Problems in other mods do not count.
3. Editor leftovers are left out and listed: hidden files and folders (`.git/`, `.godot/`), `*.import`,
   `*.uid`, `*.bak`, `*.tmp`, `*.orig`, `*~`, `Thumbs.db`, `desktop.ini`. A symbolic link refuses the export.
4. The archive holds the mod folder itself (`better_wheels/mod.json`, …) plus `better_wheels/package.json`.
   Exporting the same files twice gives the same bytes.

In the release notes, list the dependencies with versions, any conflicts, and every part you override.

### package.json

```json
{
  "package_format": 1,
  "id": "better_wheels",
  "name": "Better Wheels",
  "version": "1.2.0",
  "description": "Sport and off-road wheels.",
  "authors": ["Ada"],
  "dependencies": [{ "id": "vanilla", "version": "^0.1.0" }],
  "conflicts": [{ "id": "old_wheel_pack" }],
  "min_game_version": "0.1.0",
  "min_protocol_version": 1,
  "files": [
    { "path": "mod.json", "size": 212, "sha256": "9f2c…" },
    { "path": "parts/wheels/sport_wheel.json", "size": 1830, "sha256": "41aa…" }
  ],
  "sha256": "c07e…",
  "signature": ""
}
```

| Key | Meaning |
|-----|---------|
| `package_format` | Package format, `1`. A newer format is refused (`MD1306`). |
| `id`, `name`, `version`, `description`, `authors`, `dependencies`, `conflicts` | Copied from `mod.json`. `id` and `version` must match it and the archive's folder. |
| `min_game_version` | Game version that exported the package; older games refuse it (`MD1306`). |
| `min_protocol_version` | AI endpoint protocol version at export; older games refuse it (`MD1306`). |
| `files` | Every file except `package.json`, sorted by path: path relative to the mod folder, size in bytes, SHA-256 (lowercase hex). |
| `sha256` | The package hash: SHA-256 of the text `"<sha256>  <path>\n"` for each file, sorted by path. That is exactly what `sha256sum` prints, so `find . -type f ! -name package.json -printf '%P\n' \| LC_ALL=C sort \| xargs sha256sum \| sha256sum` inside the mod folder reproduces it. |
| `signature` | Empty. Reserved for a mod store to sign `sha256`; the game does not check it yet. |

### Installing

Press **Install…** in the Mod Manager and pick a `.zip`. The game checks the archive before it changes
anything:

- **Safe paths only**: one top folder named after the mod; no absolute paths, drive letters, `:`,
  backslashes, `..`, `.` or empty segments, names ending in `.` or a space, two paths that differ only in
  letter case, or symbolic links (`MD1302`).
- **Size limits** against zip bombs: 512 MiB archive, 20 000 entries, 256 MiB per file, 1 GiB in total, and
  no file over 1 MiB that compresses more than 200 to 1 (`MD1303`).
- **Integrity**: the files in the archive and in `files` must be the same set (`MD1305`), every file's size
  and SHA-256 must match, and the package hash must match the file list (`MD1304`). Extraction never reads
  more than the listed size, whatever the zip header claims.
- **Compatibility**: `mod.json` must agree with `package.json`, and the game must be new enough (`MD1306`).
- **Dependencies**: each must be installed in a version in range, or the install is refused (`MD1307`);
  install them first. Conflicts with installed mods, and installed mods that need a different version of
  this one, are warnings shown in the question (`MD1308`, `MD1307`).

When everything checks out the game asks **Install** (new mod) or **Replace** (an upgrade, the same
version again, an older version, or a broken folder with the mod's id). Files are extracted into a hidden
staging folder and hashed again as they are written; the old folder is swapped out only at the end and put
back if the swap fails, so a failed install changes nothing. The installed mod keeps its `package.json`.
A new mod is enabled and loads after the others with the next world; Apply is not needed. Vanilla ships
with the game and cannot be installed or uninstalled.

You can still install by hand: unzip into `mods/` so that `mods/better_wheels/mod.json` exists.

### Uninstalling

Select a mod and press **Uninstall**. The question names the installed mods that need it, which stop
loading. Confirming deletes the mod's folder. Its entry in `mods.json` is kept, so a reinstall gets its
place in the load order back.

## 10. Packaging diagnostics

| Code | Meaning |
|------|---------|
| `MD0101` | Folder has no `mod.json`; it is skipped (warning). |
| `MD0102` | `mod.json` is invalid (missing id, name or version, bad dependency or conflict entry). |
| `MD0103` | Mod id does not match the folder name. |
| `MD0104` | Two folders use the same mod id; the second is skipped. |
| `MD0105` | A dependency is not installed. |
| `MD0106` | A dependency is installed, but its version is outside the range. |
| `MD0107` | Dependency cycle. |
| `MD0108` | A dependency failed to load. |
| `MD0109` | `format_version` is newer than this build reads. |
| `MD0110` | The mod has errors in its files and was not loaded. |
| `MD0111` | A dependency is disabled in the Mod Manager. |
| `MD0112` | An enabled mod is listed under this mod's `conflicts`. |
| `MD0113` | `mods.json` is invalid. |
| `MD0203` | Overrides target a mod that is not a dependency. |
| `MD0204` | Override for a part the target mod does not have. |
| `MD0205` | Two mods override the same part; the later one wins (warning). |
| `MD1501` | An override, patch or removal names an entry that doesn't exist (an error; a warning for removals). |
| `MD1502` | Two mods change the same bot, quest, POI or character; the later one wins (warning). |
| `MD1301` | Not a mod package: unreadable zip, no `package.json`, invalid manifest, or vanilla. |
| `MD1302` | Unsafe path or symbolic link in the archive (or the mod, on export). |
| `MD1303` | Archive or a file is over the size limits, or looks like a zip bomb. |
| `MD1304` | A file's SHA-256 or size, or the package hash, does not match `package.json`. |
| `MD1305` | The archive's files and `package.json`'s `files` list differ. |
| `MD1306` | Needs a newer game, package format or AI protocol. |
| `MD1307` | A dependency is missing or the wrong version (error), or an installed mod needs another version (warning). |
| `MD1308` | Conflicts with an installed mod (warning). |
| `MD1309` | Export refused: the mod has errors. |
| `MD1310` | An installed mod's file was changed, removed or added since it was installed (warning). |
