# Saves

A saved world is one JSON file (D8) in the user data folder's `saves/` folder, named after the world
(`My World` → `my-world.json`). Only the host saves; clients get a loaded world through the same
late-join sync as any other world. Writes go to a temporary file and replace the old save in one move.
Beside each save is a small picture of the screen when it was saved (`my-world.png`), shown in the load
list; deleting a world from the list deletes both. A host auto-saves on the interval set in Options,
Gameplay (every 10 minutes unless changed).

**Continue** on the home screen starts the newest save that loads. **Load Game** lists every world with
its picture, mode, play time (its `time`) and when it was last played, each with Load and Delete; Delete
asks first.

## What a save holds

| Key | What |
|-----|------|
| `format_version` | `1` |
| `name`, `saved_at` | The world's name and when it was written |
| `mode` | `survival` or `creative`; the loaded world plays in it |
| `seed` | World generation seed: terrain, scattered objects and POIs come back from it |
| `time` | Seconds the world has been simulated (the scripts' `world.time()`); the clock carries on from it when the world loads, so times scripts stored, such as a soil's last growth, stay right. Missing in older saves (0) |
| `mods`, `fingerprint` | The mods the world ran. Loading with different mods works but logs what differs |
| `destroyed` | Trees, rocks and other world objects that were destroyed |
| `creations` | Every Creation in the Creation format: parts, links, body poses and velocities, part settings, damage, container contents, crafting queues, soil (`seed`, `grown`, `water`, `stage` and `updated`, the world time growth was last worked out at), `fire`, how strongly a burning part burns (0 to 1), and `anchored`, true on a part built resting on the ground, which holds its Body still (missing in older saves, whose Creations all simulate) |
| `scripts` | Each scripted part's Lua `state` table, by creation and part id |
| `players` | Every player the host has seen, by profile id: name, inventory, health, respawn bed, position and facing, chosen character, hotbar, map markers, and `item_scripts`, their item scripts' `state` tables by item id |
| `quests` | The story: each started quest's progress, story flags and revealed POIs (see [Quests](quests.md)) |
| `fires` | Fires at POI markers, `[{site, marker, strength}]` by POI index and marker index; a put-out one has strength 0 and stays out. Missing in older saves, whose POI fires are lit again (see [Fire](survival.md#fire)) |

Unknown keys are kept as they are, so later chunks can add their own.

## Loading

- A Creation that no longer reads (a part whose mod was removed) is skipped with a warning; the rest of
  the world loads.
- An inventory slot that no longer fits (an unknown item, a slot past the end) is dropped with a warning.
- A part script's saved `state` replaces the fresh one after the script starts. A player's item script `state`
  is there before their item's script first runs (its setup already sees it).
- A player who was dead when the world was saved comes back at full health at their spawn or bed.

## Player profiles

`profile.json` in the user data folder holds a random 32-character id and a display name; `--name`
changes the name. A client sends its profile when it joins, and the host refuses a second player with
the same id. Saved players are keyed by that id, so peer ids, join order and names can change.
