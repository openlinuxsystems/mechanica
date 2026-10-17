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
    overrides/          replacements for other mods' parts, one folder per mod id
      vanilla/wheel.json
    scripts/            Lua behaviour (*.lua)
    meshes/             glTF models (*.glb, *.gltf)
    textures/           icons and textures (*.png)
    sounds/             audio (*.ogg, *.wav)
    world/worldgen.json world generation rules; the last mod in load order that ships one wins
    pois/               POI prefabs (*.json), see world-generation.md
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
- An override is a whole definition, validated like any part. Copy the original file and edit it.
- Relative asset paths in an override resolve inside **your** mod. To keep the original mesh, write
  `"mesh": "vanilla:meshes/wheel.glb"`.
- When two mods override the same part, the one later in the load order wins and the Mod Manager shows
  a warning (`MD0205`). If your mod deliberately builds on another mod's override, depend on that mod:
  then yours always loads later and no warning is shown.

## 7. Load order and the Mod Manager

Players manage mods in the in-game **Mod Manager** (`scenes/ModManager.tscn`; it moves to the main menu
when menus are built). It lists every installed mod in load order and shows, for the setup on screen:

- a checkbox per mod. Enabling a mod also enables what it depends on. Disabling a mod also disables the
  mods that depend on it. Vanilla cannot be disabled.
- **Move up** and **Move down**. A mod always stays below the mods it depends on, so a move stops there.
  Mods with no dependency between them load in the order shown, which decides which override wins.
- each mod's status (loaded, disabled, not loaded and why, overrides won or lost) and details.
- every error, warning and conflict the setup would load with, before anything is saved.

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

1. Load your mod in the game and open the Mod Manager. Fix every error and warning it lists.
2. Remove editor leftovers: `.import` files, backups, `.godot/` folders.
3. Zip the mod **folder itself** so the archive contains `better_wheels/mod.json`, not a bare `mod.json`
   or an extra parent folder.
4. Name the archive `<id>-<version>.zip`, for example `better_wheels-1.2.0.zip`.
5. In the release notes, list the dependencies with versions, any conflicts, and every part you override.

Players install by unzipping into `mods/` next to the game executable, so `mods/better_wheels/mod.json`
exists, then enabling the mod in the Mod Manager.

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
