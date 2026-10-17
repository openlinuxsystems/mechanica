# External AI Endpoint and Record/Replay

The host can open an endpoint that lets an outside program (an AI agent, a test script, a bot you
write in any language) play through the same Action API players use. Every call is validated by
the host exactly like a player's; external agents get no shortcut around the rules, only the ones
that apply to bots and scripts (no reach limit, since they have no body).

## Starting it

```sh
godot --path game -- --host --ai                  # ws://127.0.0.1:27461
godot --path game -- --host --ai 5000             # another port
godot --path game -- --host --ai --ai-bind 0.0.0.0 --ai-token s3cret
```

The endpoint listens on loopback by default. Listening on any other address needs a token, given
with `--ai-token` or the `MECHANICA_AI_TOKEN` environment variable (the variable keeps it out of
the process list). Only the host runs the endpoint: a game that joins someone else closes it.
Up to 8 agents can connect at once.

## Protocol

JSON-RPC 2.0 over WebSocket text messages. Batches and notifications (requests without an `id`)
work as the JSON-RPC spec says. Comments and trailing commas are accepted.

Every Action API action is a method with the same name, and its `params` are the action's named
arguments (snake_case). The answer's `result` is the action's value, or `null`.

```json
→ {"jsonrpc": "2.0", "id": 1, "method": "PlacePart", "params": {"part": "drive_test:block", "cell": [0, 0, 0], "position": [2, 0.75, 12]}}
← {"jsonrpc": "2.0", "result": {"creation": 1, "part": 1}, "id": 1}
```

Built-in methods:

| Method | Params | Result |
|--------|--------|--------|
| `mechanica.auth` | `{"token": "..."}` | `true`; needed first when the host set a token |
| `mechanica.info` | none | game name, `protocol_version`, `tick_rate`, `actions`, `queries` (read-only actions) |
| `mechanica.record.start` | none | starts recording the host's Action API calls |
| `mechanica.record.stop` | none | the recording (see below) |
| `mechanica.replay.start` | `{"recording": {...}, "timed": true}` | `{"total": n}` calls it will replay |
| `mechanica.replay.status` | none | `running`, `dispatched`, `total`, `mismatches` |
| `mechanica.replay.stop` | none | stops a replay in progress |

Errors carry the Action API code in `error.data.code`:

| `error.code` | `data.code` | Meaning |
|--------------|-------------|---------|
| -32700 | | not valid JSON |
| -32600 | | not a JSON-RPC request, an empty or oversized batch (256 at most) |
| -32601 | `unknown_action` | no such method |
| -32602 | `bad_args` | params are not an object or don't fit the action |
| -32001 | `rejected` | the host's validation refused it; `message` says why |
| -32002 | `rate_limited` | too many calls (600 per second per connection, bursts of 1200) |
| -32003 | | call `mechanica.auth` first, or the token was wrong |
| -32004 | | record/replay can't do that now (nothing recording, no replay) |
| -32603 | `internal_error` | the action failed on the host |

## Actions for driving

These are the generic motion primitives (decision D2). Part scripts call the same actions (see the
[Lua API Reference](lua-api.md)); seats, engines and controllers (chunk 10) will be Lua parts built on them; players don't call them directly and are refused if they try.

| Action | Arguments | Notes |
|--------|-----------|-------|
| `ListCreations` | none | `[{id, parts, position}]` |
| `GetCreation` | `creation` | parts, bodies (`index`, `parts`, `mass`, `position`, `rotation` `[x,y,z,w]`, `forward`, `up`, velocities), joints (`part`, `other`, `type`, `locked`, `position` in rad or m, `min`, `max`) and `part_states`: per part its `id`, `part` (definition id), `cell` and `rotation` (as `PlacePart` takes them), `body`, world `centre`, `level`, `damage`, and when they apply `color`, `health`, `max_health`, `fire` and changed `settings` |
| `ApplyForce` | `creation`, `force` (N, world), optional `body` (default 0), `torque` (N·m), `duration` (s, default one physics step), `at` (world point, m; default the centre of mass), `part` (the pushing part, in `body`) | a later call on the same body replaces the push; with `part`, pushes from different parts add up and a later call replaces only that part's push. An `at` off the centre of mass also twists the body (at most `max_lever_arm` m away). It ends early if the Creation is rebuilt |
| `SetBearingTarget` | `creation`, `part` (the bearing), `velocity` (rad/s about the bearing's axis, or `null` to free it), optional `max_torque` (N·m) and `angle` (rad) | the motor stays on until changed. With `angle` the bearing turns to that angle (from where it was built, within ±π and its limits) at up to `velocity` rad/s (default `default_bearing_speed`) and holds it |
| `SetPistonTarget` | `creation`, `part` (the piston), `length` (m of extension, within the joint's `min`–`max`, or `null` to free it), optional `speed` (m/s) and `max_force` (N) | moves to the length and holds it until changed |

Limits (largest force, torque, push duration, bearing speed and torque, piston speed and force, and
how hard targets are chased, `servo_gain`) are data in
`game/data/motion.json`. A Creation that is parked because nobody is near it can't be moved.

`PlacePart` and `RemovePart` build and take apart Creations as described in the building docs. The
survival actions (`UseItem`, `EquipItem`, `PickUp`, `Respawn`, ...) are listed in
[Survival](survival.md#action-api). A ripe crop is harvested with `ActivatePart` on its soil part (the soil's
Lua script decides; the result is `{items}`).

## Seeing the world

Read-only queries: they never change anything, so a dead player can still call them and recordings skip
them. "The caller" is the player the call is made for; with no player, pass the point or peer explicitly.

| Action | Arguments | Value |
|--------|-----------|-------|
| `GetWorld` | none | `seed` (and `seed_text`, exact for JavaScript), `mode`, `difficulty`, `time` (host clock, s), `tick`, `tick_rate`, `world_size`, `sea_level`, `spawn`, `players` `[{peer, name}]`, counts of `creations`, `drops`, `pois`, and `revealed_pois` |
| `GetPlayers` | none | every player as below |
| `GetPlayer` | optional `peer` (default the caller) | `peer`, `name`, `character`, `position` (feet), `eye`, `look` (unit vector), `yaw` and `pitch` (rad; yaw 0 looks along −z, turning left is positive), `velocity`, `on_floor`, `swimming`, `crouched`, `seat` `{creation, part}`, `held` (tool in hand: `hammer`, `lift_remote`, `sledgehammer`, …), `held_item` (scripted item), `lift` `{owner, position, level, creation}`, `health`, `max_health`, `dead` |
| `Raycast` | optional `from` (default the caller's eyes), `direction` (default where the caller looks), `range` (m, default 100, at most 1000), `ignore_player` (default the caller when casting from their eyes) | `{hit: false}` or `hit`, `kind` (`player`, `part`, `object`, `bot`, `ground`), `point`, `distance`, and `player`, `creation` + `part` + `part_type`, `object` + `object_type`, or `bot` |
| `SampleTerrain` | `points` `[[x, z], …]`, or `min` and `max` `[x, z]` with `step` (m); at most 4096 samples | `sea_level` and `samples` `[{x, z, height, biome, slope, water_depth, poi}]`, grids row by row with x fastest |
| `GetObjects` | optional `center` (default the caller's feet), `radius` (m, default 32, at most 256), `type` (`tree`, `rock_node`, …), `limit` (default 100, at most 1000) | standing trees and rocks nearest first: `id` (what `Sledge` and `StartRefine` take), `type`, `position`, `yaw`, `scale`, `distance`, `health` and `max_health` (null when hits can't break it), `refine`, `drops` |
| `GetDrops` | optional `center`, `radius` (m; with neither, every pile) | piles on the ground: `id` (what `PickUp` takes), `position`, `items`, `placed`, `expires_in` (s, null when placed), `distance` |
| `GetPois` | optional `all` (default false; players can't set it) | POIs the story has revealed (or all): `index`, `id`, `name`, `centre`, `radius`, `yaw`, `revealed`, `markers` `[{type, ref, position, yaw}]` |

## Catalog

Ids in the game are qualified (`mod_id:id`); these queries list them so an agent never guesses. They read
the content loaded now, so they follow mod reloads.

| Action | Arguments | Value |
|--------|-----------|-------|
| `ListParts` | optional `category`, `mod`, `search` (any case, id or name) | buildable parts, by id: `id`, `name`, `category`, `placeable`, `stack_size`, `mass`, `size` (cells), `durability`, `settings` (ids), `description` |
| `ListItems` | as `ListParts` | the same for everything that fits in an inventory, buildable or not |
| `GetPart` | `part` | `id`, `mod`, `placeable`, `definition` (the part file as loaded, see [Part data schema](part-data-schema.md): sockets, joints, `settings` with type, range, options and default, which is what `ConfigurePart` accepts), `made_by`, `used_in` and `crafts` (recipe ids) |
| `ListRecipes` | optional `station`, `makes`, `uses` | `id` (what `Craft` takes), `name`, `stations`, `inputs`, `outputs`, `time` |
| `ListBotTypes` | none | `id` (what `SpawnBot` takes), `mod`, `name`, `health`, speeds, `sight`, size, `melee_immune`, `passive`, `raid_cost`, `attacks`, `drops`, `script`, `behavior` (the tree) |
| `GetQuestBook` | none | quests: `id`, `name`, `summary`, `requires`, `steps` (`id`, `text`, `kind`, `poi`, `marker`, `target`, `count`, `dialogue`, `choices` with the ids `ChooseQuestOption` takes), rewards (`items`, `reveal`, `flags`) and `outro` |
| `ListCharacters` | none | `id` (what `ChooseCharacter` takes), `name`, `description` |

## Recording and replay

The host records every state-changing call from every player, bot, script and agent (queries are
left out) with the physics tick it ran on:

```sh
godot --path game -- --host --record session.json   # file rewritten every 5 s and on quit
godot --path game -- --replay session.json          # replays it on a fresh host
```

or over the endpoint with `mechanica.record.start` and `mechanica.record.stop`. A recording is JSON:

```json
{
  "format_version": 1,
  "tick_rate": 60,
  "truncated": false,
  "actions": [
    { "tick": 37, "peer": 1, "source": "external", "action": "PlacePart", "args": { ... }, "result": { "ok": true, "value": { ... } } }
  ]
}
```

A replay dispatches the recorded calls again through the same validator, as the caller who asked
for the replay (an external agent), with the same tick spacing unless `timed` is false. Calls that
failed when recorded are skipped. Each replayed call whose answer differs from the recorded one is
listed in `mismatches`; a replay into a fresh world with the same mods reproduces the recorded ids
and answers. Physics is replayed by repeating the inputs, not by storing poses, so a replay into a
different world (other Creations, players in the way) can diverge, and the mismatches show where.

## Example agent

[`tools/ai/drive_car.mjs`](../tools/ai/drive_car.mjs) (Node 22+, no packages) builds a four-wheeled
car and drives it: forward with both sides' bearings, then on the spot by running the sides against
each other. Until vanilla wheels and bearings exist, copy the fixture mod
`game/tests/remote/mods/drive_test` into `mods/` to give it parts:

```sh
godot --path game -- --host --ai
node tools/ai/drive_car.mjs --mod drive_test
```

The headless test `game/tests/remote/RemoteTest.tscn` does the same with a host and a client, and
also checks errors, authentication, recording and a replay on a second host.
