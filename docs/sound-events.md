# Sound Events

Every sound in Mechanica is a **sound event**: a named entry in a mod's `sounds/*.json` that says
which audio files to play and how. Parts, tools, scripts and the UI ask for an event by id and never
name an audio file directly, so a mod can change how anything sounds by replacing the event.
Like all mod data, keys are `snake_case`, units are SI (metres, seconds, dB), and comments and
trailing commas are allowed.

You can write these files by hand, but the **Sounds** tab of the Part Editor
([Getting Started](getting-started.md#8-sounds)) does it for you, checks them with the same rules the
game uses, and lets you hear the result before saving.

## Files

A mod keeps its events in any number of `sounds/**/*.json` files, each holding a list:

```json
{
  "events": [
    {
      "id": "engine_idle",
      "description": "Small engine running with no load.",
      "clips": ["sounds/engines/engine_idle_loop.ogg"],
      "loop": true,
      "volume_db": -4,
      "max_distance": 60,
      "parameters": [
        { "name": "rpm", "min": 0, "max": 1, "pitch": [0.7, 1.8], "volume_db": [-8, 0] }
      ]
    },
    {
      "id": "smash_hit",
      "clips": ["sounds/tools/smash_hit_1.ogg", "sounds/tools/smash_hit_2.ogg"],
      "pitch_variation": 0.08,
      "volume_variation_db": 2
    }
  ]
}
```

Audio files are `.ogg` (Vorbis) or `.wav` (PCM), loaded at runtime from the mod folder. Paths are
relative to the mod root, or `other_mod:path` for another mod's file (that mod must be a dependency).
`..`, absolute paths and `res://` are refused.

An event's game-wide id is `mod_id:id`. A file with any error is left out whole, with a message saying
why; the rest of the mod still loads, because a missing sound never stops a game.

## Event fields

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `id` | string | required | Lowercase `snake_case`, unique in the mod. `other_mod:id` replaces that mod's event (see [Replacing sounds](#replacing-sounds)). |
| `description` | string | none | What the sound is, for modders. |
| `clips` | list of paths | required | 1 to 32 audio files. Each play picks one at random. |
| `bus` | `sfx`, `ui`, `ambient`, `music` | `sfx` | Mixer bus, so players can set each volume. |
| `loop` | bool | `false` | Plays until stopped. Only loops take `parameters`. |
| `volume_db` | number, -80 to 24 | 0 | Base volume in dB (0 is the file as recorded). |
| `pitch` | number, 0.01 to 16 | 1 | Base pitch scale (2 is an octave up). |
| `volume_variation_db` | number, 0 to 24 | 0 | Each play shifts the volume by up to this much either way. |
| `pitch_variation` | number, 0 to < 1 | 0 | Each play scales the pitch by up to this fraction either way (0.05 is ±5 %). |
| `spatial` | bool | `true`, except `ui` and `music` | Heard from a place in the world: panned, and quieter with distance. |
| `min_distance` | metres > 0 | 1 | Full volume within this distance. |
| `max_distance` | metres > `min_distance` | 40 | Silent beyond this distance. Also how far the host sends it. |
| `rolloff` | `inverse`, `inverse_square`, `linear` | `inverse` | How the volume falls between the two distances. |
| `parameters` | list | none | Controls that change a playing loop. See below. |
| `max_instances` | integer, 1 to 64 | 8 | Most copies playing at once. A new one-shot over the limit cuts the oldest; a new loop over it is not started. |
| `cooldown` | seconds, 0 to 60 | 0 | Shortest time between two starts of this event. |

### Distance

Between `min_distance` and `max_distance`, the gain at distance *d* is `min/d` for `inverse`,
`(min/d)²` for `inverse_square` and `(max - d)/(max - min)` for `linear`. Beyond `max_distance` it
is 0. Every peer computes this the same way, so what you hear matches what the host decides is in
earshot.

### Loop parameters

A parameter maps a value the game or a script sets (engine load, wheel speed, wind) onto pitch and
volume while the loop plays.

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `name` | string | required | `snake_case`, unique in the event. |
| `min`, `max` | number | 0, 1 | Range of the value. Values outside it are clamped. |
| `default` | number | `min` | Value when the loop starts. |
| `pitch` | `[low, high]` | none | Pitch scale at `min` and at `max`, interpolated in between. |
| `volume_db` | `[low, high]` | none | dB added at `min` and at `max`. |

With several parameters, their pitch scales multiply and their dB add up.

## Part sounds

A part's `sounds` object binds local sound names to event ids:

```json
"sound_material": "wood",
"sounds": { "remove": "plank_pop", "start": "engine_start" }
```

A bare id means the part's own mod's event when it has one, otherwise vanilla's. The game itself
plays these names:

| Name | When | Without a binding |
|------|------|-------------------|
| `place` | The part is placed. | `place_<sound_material>`, else `part_attach` |
| `remove` | The Hammer takes it off. | `remove_block` for blocks, else `part_detach` |
| `impact` | A Sledgehammer hits it without breaking it. | `impact_<sound_material>` |
| `break` | It breaks. | `break_<sound_material>` |
| `start` | A crafting station begins working its queue. | nothing |
| `work` | A loop while the station works; it stops when the queue empties or the output is full. | nothing |
| `complete` | The station finishes a craft. | nothing |

Any other name is for the part's Lua script to play. The loader warns about a part that names an
event nobody registered (MD0804).

## Character sounds

Footsteps, jumps, landings, swimming and taking a tool in hand play through the character's own `sounds`
bindings, the same way as a part's; see [Player Characters](characters.md#sounds). Each peer plays them
for every player near it from that player's movement, so they are never sent over the network.

## Replacing sounds

An event whose id is `other_mod:id` replaces that mod's event everywhere it is used. The other mod must
be a dependency, so it loads first. When two mods replace the same event, the one later in the load
order wins and the loader warns (MD0803).

## Multiplayer

The host decides every sound that comes from the world (placing, smashing, breaking, a part's loop)
and sends it only to players within `max_distance` plus 5 m. Feedback for one player's own hand (the
Hammer's swing, the Weld Tool's spark, a refused action, UI clicks) plays on that player's machine and
is never sent. Loops belong to the host: it starts them on a point or a part, sends parameter changes
20 times a second, stops them when the part goes, and gives every running loop to a player who joins.

## Vanilla placeholders

Vanilla's events cover every sound id in the catalog. Their audio is original placeholder synthesis
made by `tools/sounds/make_placeholders.py` (Python with numpy, plus ffmpeg for Vorbis), seeded per
id so it rebuilds byte for byte. Run it to regenerate `mods/vanilla/sounds/`, or with `--check` to
list the events. Replace a placeholder by pointing its event at a new file.

## Error codes

| Code | Meaning |
|------|---------|
| MD0801 | A sound event value is missing or invalid, or a file was left out because of its errors. |
| MD0802 | Two events in a mod share an id. |
| MD0803 | Two mods replace the same event (warning). |
| MD0804 | A part names a sound event nobody registered (warning). |
