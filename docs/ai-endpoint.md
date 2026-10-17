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
godot --path game -- --host --ai --ai-token player --ai-admin-token root --ai-observe-token watch
```

The endpoint listens on loopback by default. Listening on any other address needs a token, given
with `--ai-token` or the `MECHANICA_AI_TOKEN` environment variable (the variable keeps it out of
the process list). Only the host runs the endpoint: a game that joins someone else closes it.
Up to 8 agents can connect at once.

## Scopes

Each connection holds scopes that decide what it may call:

| Scope | Allows |
|-------|--------|
| `observe` | queries (read-only actions), `mechanica.info`, `mechanica.describe`, events, recording |
| `play` | every state-changing action, validated like anyone else's |
| `admin` | `SpawnPart`, `ClearCreations`, `SpawnBot`, `DespawnBot`, `MoveBot`, `BotAttack`, `StartRaid`, `GiveItems`, replays, and admin methods added by modules (mod tools, session control) |

Without any token every connection has all three. With tokens, a connection gets the scopes of the
token it gives to `mechanica.auth`:

| Token | Variable | Scopes |
|-------|----------|--------|
| `--ai-token` | `MECHANICA_AI_TOKEN` | all three when no admin token is set, otherwise observe and play |
| `--ai-admin-token` | `MECHANICA_AI_ADMIN_TOKEN` | all three |
| `--ai-observe-token` | `MECHANICA_AI_OBSERVE_TOKEN` | observe |

A call outside the connection's scopes fails with -32005 (`data.code` `forbidden`). `mechanica.info`
lists the connection's scopes.

## Protocol

JSON-RPC 2.0 over WebSocket text messages. Batches and notifications (requests without an `id`)
work as the JSON-RPC spec says. Comments and trailing commas are accepted.

Every Action API action is a method with the same name, and its `params` are the action's named
arguments (snake_case). The answer's `result` is the action's value, or `null`.

```json
→ {"jsonrpc": "2.0", "id": 1, "method": "PlacePart", "params": {"part": "drive_test:block", "cell": [0, 0, 0], "position": [2, 0.75, 12]}}
← {"jsonrpc": "2.0", "result": {"creation": 1, "part": 1}, "id": 1}
```

Built-in methods (protocol version 2):

| Method | Scope | Params | Result |
|--------|-------|--------|--------|
| `mechanica.auth` | | `{"token": "..."}` | `true`; needed first when the host set a token, grants its scopes |
| `mechanica.info` | observe | none | game name, `protocol_version`, `tick_rate`, `tick` (current physics tick), this connection's `scopes`, `peer` and `source`, and the names of every `actions`, `queries`, `methods` and event `topics` |
| `mechanica.describe` | observe | optional `{"names": [...]}` | every method with its `kind` (action, query or method), `group` (the game area, like `survival`, or the method prefix), `scope`, `description` and a JSON Schema of its `params`, plus the event `topics` and error codes |
| `mechanica.subscribe` | observe | `{"topics": [...]}` | the topics now subscribed; see [Events](#events) |
| `mechanica.unsubscribe` | observe | optional `{"topics": [...]}` | the topics still subscribed (none given stops all) |
| `mechanica.join` | play | `{"name": "..."}` | `peer`, `profile_id`, `name`; spawns a player of your own (see [Playing as a player](#playing-as-a-player)) |
| `mechanica.leave` | play | none | removes your player (it is saved) |
| `mechanica.record.start` | observe | none | starts recording the host's Action API calls |
| `mechanica.record.stop` | observe | none | the recording (see below) |
| `mechanica.replay.start` | admin | `{"recording": {...}, "timed": true}` | `{"total": n}` calls it will replay |
| `mechanica.replay.status` | observe | none | `running`, `dispatched`, `total`, `mismatches` |
| `mechanica.replay.stop` | admin | none | stops a replay in progress |

`mechanica.describe` is the way to build an AI's tool list: each entry's `params` is a JSON Schema
(snake_case properties, `required`, `additionalProperties: false`, vectors as `[x, y, z]` arrays, grid
rotations as strings like `"+x+y"`), and its `description` comes from the game's own documentation.

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
| -32004 | | can't be done now (nothing recording, no replay, busy) |
| -32005 | `forbidden` | the connection lacks the method's scope |
| -32603 | `internal_error` | the action failed on the host |

## Events

After `mechanica.subscribe`, the host pushes JSON-RPC notifications (no `id`) on the same socket:

```json
← {"jsonrpc": "2.0", "method": "mechanica.event", "params": {"topic": "hurt", "tick": 5120, "data": {"peer": 1, "amount": 12, "cause": "weapon"}}}
```

| Topic | Data |
|-------|------|
| `actions` | every state-changing Action API call by players, bots and agents (not Lua scripts): `peer`, `source`, `action`, `args`, `ok`, `code`, `message`, `value` |
| `messages` | Lua prints, warnings and errors from part, item and bot scripts (`kind`, `source`, `script`, `creation`, `part`, `text`), and host failures while handling an agent's call (`kind` `error`, `source` `endpoint`, `method`, `text`) |
| `hurt` | a player took damage: `peer`, `amount`, `cause` |
| `vitals` | a player's health changed, they died or respawned: the `GetVitals` view |
| `dialogue` | a quest character spoke: `quest`, `lines` |
| `quest` | quest progress changed: the `GetQuests` state |
| `raid` | a raid started, advanced or ended: the `GetRaid` view |
| `bots` | `kind` `spawned`, `removed`, `attacked` or `crop_destroyed`, with the bot, attack or crop |
| `drop` | `kind` `changed` or `removed`, with the drop |
| `chat` | a chat line |
| `fire` | a fire went out |
| `seats` | someone sat down or stood up: `seat`, `seated` |
| `projectiles` | `kind` `launched` or `landed` |
| `mods`, `reports` | reserved for the mod tools and AI reports |

A connection that falls more than 4096 notifications behind loses the oldest and then receives one
`events_dropped` notification with the `count` it missed.

## Playing as a player

Without joining, an agent's actions act as an external agent on the host's own peer. `mechanica.join`
gives the connection a player body of its own instead, with its own inventory, health, quests and
reach, which human players see with " (AI)" after its name. From then on every action the connection
calls acts as that player (`mechanica.info` reports its `peer` and `source: "player"`), exactly as if a
human on another computer had pressed the keys.

```json
→ {"jsonrpc": "2.0", "id": 1, "method": "mechanica.join", "params": {"name": "Grok"}}
← {"jsonrpc": "2.0", "result": {"peer": 2130706432, "profile_id": "3f0c…", "name": "Grok (AI)"}, "id": 1}
→ {"jsonrpc": "2.0", "id": 2, "method": "SetPlayerInput", "params": {"move_z": 1, "yaw": 1.57, "sprint": true, "ticks": 120}}
```

`SetPlayerInput` holds movement for `ticks` physics ticks (60 per second, at most `max_input_ticks`)
and then the player stands still, still looking the same way: `move_x` and `move_z` (-1 to 1, relative
to the look direction), `yaw` and `pitch` in radians (left out keeps the current look; yaw 0 looks down
-Z), and `jump`, `sprint` and `crawl`. A new call replaces the one still running. Only a connection that
joined may call it.

The saved player follows the name, case-insensitively: leave and join again as "Grok" and you get its
inventory, health and position back. One connection plays one player, and two can't share a name.
Closing the socket removes the player like `mechanica.leave`. The host's limits are in
`game/data/agents.json` (`max_agents`, `max_input_ticks`, `name_suffix`); agents also take player slots,
so a full world refuses them.

## Adding methods (for game code)

Game systems add endpoint methods outside the Action API as an `IRpcModule`
(`src/Mechanica.Core/Remote/RpcModules.cs`): each `RpcMethod.Create<TParams>(name, scope, description,
handler)` gets a lowercase dotted name (`mods.validate`), a scope, and a snake_case params record that
`mechanica.describe` turns into a schema. Handlers throw `RpcException` with an `RpcErrors` code to fail. `RpcMethod.CreateAsync` takes a handler that returns a `Task` (for work that waits for Godot frames, such as baking a mesh); the endpoint answers when it completes, and inside a batch such a call is answered on its own afterwards. Module params are limited only by the 4 MiB message size, not the Action API's 16 KiB.
Add the module to `Main.Ai.Modules` before the endpoint starts listening, then run `tools/ai/gen-docs.sh` so the
AI docs in [docs/ai](ai/README.md) list it (the headless test `game/tests/aidocs` fails until they do). Host code publishes events
with `Main.Ai.Events.Publish(topic, data)`; new topics go in `RemoteEvents.Topics`.

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

## Mod packages

Admin methods that share mods as verified packages, through the same code as the Mod Manager's
Export, Install and Uninstall ([Packaging Guide](packaging-guide.md) §9 has the format). Packages live in
one folder (`user://mod_packages`; on Linux `~/.local/share/godot/app_userdata/Mechanica/mod_packages/`):
methods name a package by its file name there, never by path. Installs and uninstalls take effect with the
next world and are published on the `mods` topic (`action` `installed` or `uninstalled`). Every
`problems` entry is `{severity, code, file, message}` with the codes from the Packaging Guide.

| Method | Params | Result |
|--------|--------|--------|
| `package.list` | none | `folder`, `packages` `[{file, size}]` |
| `package.inspect` | `file` | `valid`, `can_install`, `change` (`new`, `upgrade`, `reinstall`, `downgrade`, `replace_broken`), `installed_version`, `manifest` (the package.json), `problems` |
| `package.export` | `id` | `ok`, `file`, `version`, `sha256`, `files` (count), `skipped`, `problems`; a mod with errors is not exported |
| `package.install` | `file`, optional `replace` (default false) | `ok`, `id`, `version`, `change`, `installed_version`, `needs_replace` (true when it stopped because a copy is installed), `problems` |
| `package.uninstall` | `id` | `ok`, `id`, `dependents` (installed mods that need it and stop loading), `problems`; vanilla can't be uninstalled |
| `package.verify` | `id` | `from_package`, `unchanged`, `problems` (files changed, missing or added since install) |

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
