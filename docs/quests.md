# Quests and Dialogue

A mod's story is its `quests/**/*.json` (one quest per file, in any subfolder; the file name is the quest id) and the POIs those
quests point at (`pois/*.json`, see [World Generation](world-generation.md)). Vanilla authors both through
the story batch `mods/vanilla/editor/batches/story.json`: edit the batch and re-run
`tools/author-vanilla.sh`, which checks every POI and quest the way the loader does and rewrites the files.

There is no in-game quest editor: write quests as JSON files, or (better) as a [story batch](#story-batches),
which checks them like the loader does. [Your first quest](#your-first-quest) walks through one.

To change or remove another mod's quest, override it, patch it or list it in `remove.json`; see the [Packaging Guide](packaging-guide.md#overriding-bots-quests-pois-and-characters).

Quests are shared by everyone in the world. The host runs them and sends the state to every client; a save
keeps it under `quests` (see [Saves](saves.md)).

## A quest

```json
{
  "name": "Faction Contact",
  "summary": "Meet the faction at their outpost.",
  "requires": ["first_farm"],
  "steps": [
    { "id": "find", "text": "Find the faction outpost.", "objective": { "type": "reach", "poi": "faction_outpost", "radius": 20 } },
    { "id": "talk", "text": "Talk to the envoy.", "objective": { "type": "interact", "poi": "faction_outpost", "marker": "npc", "ref": "faction_envoy" },
      "dialogue": [{ "speaker": "Grazer Envoy", "text": "You're the one from the wreck." }] },
    { "id": "decide", "text": "Ally with the faction, or not.", "objective": { "type": "choose" },
      "choices": [
        { "id": "ally", "text": "Ally with them.", "flags": { "faction_stance": "ally" } },
        { "id": "hostile", "text": "Refuse.", "flags": { "faction_stance": "hostile" } }
      ] }
  ],
  "rewards": { "items": [{ "item": "component_kit", "count": 4 }], "reveal": ["ai_core_facility"], "flags": { "met_faction": "yes" } },
  "outro": [{ "speaker": "Logbook", "text": "Faction contact logged." }]
}
```

| Key | Meaning |
|-----|---------|
| `name`, `summary` | Shown in the quest log. `name` is required. |
| `requires` | Quests that must be done first (`other_mod:quest` for another mod's). A quest with none starts when the world does. Loops are errors. |
| `steps[]` | Done in order. Each has an `id`, the log `text`, an `objective`, and optional `dialogue` said when the step begins. |
| `rewards.items` | Given to every player in the session when the quest is done. |
| `rewards.reveal` | POIs shown in every player's quest log, with distance and direction, and on the Logbook map. |
| `rewards.flags` | Story flags set when the quest is done. |
| `outro` | Lines said when the quest is done. |

Dialogue lines are `{ "speaker", "text" }`. They show in the dialogue box at the bottom of the screen for
every player, one after another.

## Objectives

| `type` | Done when | Keys |
|--------|-----------|------|
| `reach` | Any player stands within `radius` (default: the POI's radius) of a placed `poi` | `poi`, `radius` |
| `interact` | Players use `count` different markers of type `marker` (and `ref`, if given) at the `poi` with E | `poi`, `marker`, `ref`, `count` |
| `have_item` | Any player holds `count` of `item` | `item`, `count` |
| `place` | Players place `count` parts (of `part`, if given) | `part`, `count` |
| `craft` | Players craft `count` items (of `recipe`, if given) | `recipe`, `count` |
| `drive` | Players seated in a moving Creation cover `distance` metres | `distance` |
| `crops` | `count` crops are planted in the world at once | `count` |
| `raid` | `count` raids end | `count` |
| `choose` | A player picks one of the step's `choices` in the logbook (L) | `choices` on the step |
| `extinguish` | Players put out `count` of the fires burning at the `poi`'s `marker` markers (default `fire`) with water ([Fire](survival.md#fire)) | `poi`, `marker`, `count` |

A choice has an `id`, the button `text`, `flags` to set and optional `dialogue`. Whatever objective a step
has, players near an `npc` marker of the quest's POI can press E to hear the step's dialogue again.

## Markers quests use

POI markers are points with a `type` and an optional `ref`. Quests read any type; vanilla uses
`fire` (a fire burns at each one from the start, for `extinguish`), `item` (with the part's id as `ref`), `npc` (with a character id as `ref`, such as
`farmer`, who is drawn standing there when the id names a [non-player character](characters.md#non-player-characters)) and `core` (the AI core at the AI Core Facility). The prompt on screen says "talk" for an
`npc` marker and "use the ..." with the `ref`'s name for the rest of the `interact` markers.

## Actions

| Action | Arguments | What |
|--------|-----------|------|
| `QuestInteract` | none | Uses the nearest marker in reach (4 m) that an active step needs, or else talks to an `npc` marker in reach |
| `ChooseQuestOption` | `quest`, `choice` | Picks a choice of a `choose` step |
| `GetQuests` | none | The state: `quests` (progress by quest), `flags` and `revealed` |

Errors in quest files use code `MD1101`.

## Story batches

A story batch is one file that holds a mod's POIs and quests. It lives in `editor/batches/` (vanilla's is
`mods/vanilla/editor/batches/story.json`), and it is told apart from a Part Editor batch by its `pois` or
`quests` key:

```json
{
  "format_version": 1,
  "pois": [
    { "file": "pois/scrap_camp.json", "poi": { "name": "Scrap Camp", "radius": 12.0, "placement": {} } }
  ],
  "quests": [
    { "file": "quests/scrap_camp.json", "quest": { "name": "The Scrap Camp", "steps": [] } }
  ]
}
```

| Key | Meaning |
|-----|---------|
| `format_version` | `1`. |
| `pois[]` | `file` is the file to write, `pois/<id>.json`; `poi` is the POI exactly as in that file ([World Generation](world-generation.md)), without an `id`: the id comes from the file name. |
| `quests[]` | `file` is `quests/<id>.json`; `quest` is the quest as above, again without an `id`. |

A batch needs at least one POI or quest. To run it, open the Part Editor, press **Run batch…** and pick the
file, or run it without the game window:

```sh
godot --headless --path game -- --part-batch mods/my_mod/editor/batches/story.json
```

The POIs are written first, so the quests after them can name them. Each one goes through the same checks as
when the mod loads; one that fails is skipped with its problems listed, and the rest are still written. A
mistake in the batch itself (a file name that is not `pois/<id>.json` or `quests/<id>.json`, an `id` given
inside, a missing `poi` or `quest`, an unknown `format_version`) stops it before anything is written, with code
`MD0503`. Running it again rewrites the same files, so keep the batch as the place you edit them.

## Your first quest

This makes a mod whose quest follows vanilla's story: once the Workshop quest is done, it sends the
players to a small camp where someone waits to talk.

1. Make a mod (Mod Manager, **New mod…**) with `vanilla` as a dependency, say `my_story`.
2. Add `editor/batches/story.json` to it:

   ```json
   {
     "format_version": 1,
     "pois": [
       {
         "file": "pois/scrap_camp.json",
         "poi": {
           "name": "Scrap Camp",
           "radius": 12.0,
           "size": [6.0, 3.0, 6.0],
           "placement": { "spacing": [600.0, 900.0], "biomes": ["meadow", "field"], "priority": 10, "clearance": 16.0 },
           "terrain": [{ "op": "flatten", "radius": 8.0, "blend": 6.0 }],
           "pieces": [{ "shape": "box", "size": [6.0, 0.3, 6.0], "at": [0.0, 0.15, 0.0], "color": "#6f6a5e" }],
           "markers": [{ "type": "npc", "ref": "camp_keeper", "at": [0.0, 0.3, -2.0] }]
         }
       }
     ],
     "quests": [
       {
         "file": "quests/scrap_camp.json",
         "quest": {
           "name": "The Scrap Camp",
           "summary": "Someone is living out in the meadow.",
           "requires": ["vanilla:workshop_outpost"],
           "steps": [
             { "id": "find", "text": "Find the scrap camp.", "objective": { "type": "reach", "poi": "scrap_camp" } },
             { "id": "talk", "text": "Talk to the camp keeper.",
               "objective": { "type": "interact", "poi": "scrap_camp", "marker": "npc", "ref": "camp_keeper" },
               "dialogue": [{ "speaker": "Camp Keeper", "text": "Another one from the wreck. Take these, you'll need them." }] }
           ],
           "rewards": { "items": [{ "item": "vanilla:component_kit", "count": 2 }] },
           "outro": [{ "speaker": "Logbook", "text": "Scrap camp logged." }]
         }
       }
     ]
   }
   ```

   Bare ids are your mod's: `scrap_camp` is `my_story:scrap_camp`. Anything from vanilla needs its prefix
   (`vanilla:workshop_outpost`, `vanilla:component_kit`). The `spacing` placement makes the world generator put
   camps in meadows and fields; a POI with `"placement": {}` is only placed by a site of the map's layout.
3. Run the batch (Part Editor, **Run batch…**). It writes `pois/scrap_camp.json` and `quests/scrap_camp.json`
   and lists any problem.
4. Try it. While testing, take `requires` out (and run the batch again) so the quest starts with the world. Turn
   the mod on in the Mod Manager and start a new world with it. Press **L** to see the quest in the Logbook; the
   compass points to the nearest camp. Walk there: when you are inside the camp the first step is done and the
   keeper's line plays. Walk up to the keeper's marker (within 4 m) and press **E** to finish the second step;
   the outro plays and every player gets two Component Kits. Check it with a client too: open the world to the
   LAN from the pause menu and join it from a second copy of the game.
5. Put `requires` back and run the batch once more. Now the quest starts as soon as vanilla's Workshop
   quest is done, also in a saved world where that quest was already finished.

