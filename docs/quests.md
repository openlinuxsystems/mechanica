# Quests and Dialogue

A mod's story is its `quests/**/*.json` (one quest per file, in any subfolder; the file name is the quest id) and the POIs those
quests point at (`pois/*.json`, see [World Generation](world-generation.md)). Vanilla authors both through
the story batch `mods/vanilla/editor/batches/story.json`: edit the batch and re-run
`tools/author-vanilla.sh`, which checks every POI and quest the way the loader does and rewrites the files.

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
`faction_envoy`) and `core` (the AI core at the AI Core Facility). The prompt on screen says "talk" for an
`npc` marker and "use the ..." with the `ref`'s name for the rest of the `interact` markers.

## Actions

| Action | Arguments | What |
|--------|-----------|------|
| `QuestInteract` | none | Uses the nearest marker in reach (4 m) that an active step needs, or else talks to an `npc` marker in reach |
| `ChooseQuestOption` | `quest`, `choice` | Picks a choice of a `choose` step |
| `GetQuests` | none | The state: `quests` (progress by quest), `flags` and `revealed` |

Errors in quest files use code `MD1101`.
