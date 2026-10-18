# Mechanica AI method reference

<!-- Generated from mechanica.describe by tools/ai/gen-docs.sh. Do not edit by hand. -->

Protocol version 2. 166 methods: 58 actions (change the world), 29 queries (only read it) and 79 endpoint methods. Every one is a JSON-RPC method of the same name whose `params` are the named arguments below (snake_case). `describe.json` beside this file is the same data as JSON Schemas, ready to become an AI's tool list. Scopes: `observe` reads, `play` acts as a player, `admin` is for debug powers ([ai-endpoint.md](../ai-endpoint.md#scopes)).

## Contents

- [Endpoint built-ins (mechanica.*)](#endpoint-built-ins-mechanica) (12)
- [Agent players](#agent-players) (2)
- [Seeing the world and the catalog](#seeing-the-world-and-the-catalog) (15)
- [Bots and raids](#bots-and-raids) (8)
- [Building](#building) (14)
- [Characters](#characters) (1)
- [Chat](#chat) (1)
- [Debug and general](#debug-and-general) (2)
- [Fire](#fire) (1)
- [Items, inventories and crafting](#items-inventories-and-crafting) (8)
- [Logic, seats and part menus](#logic-seats-and-part-menus) (8)
- [Map markers](#map-markers) (3)
- [Motion and physics primitives](#motion-and-physics-primitives) (6)
- [Quests](#quests) (3)
- [Survival](#survival) (11)
- [Using held items](#using-held-items) (4)
- [World](#world) (9)
- [Editors (editor.*)](#editors-editor) (30)
- [Lua tools (lua.*)](#lua-tools-lua) (3)
- [Mod packages (package.*)](#mod-packages-package) (6)
- [Mod tools (mods.*)](#mod-tools-mods) (12)
- [Notes, recommendations and bug reports (reports.*)](#notes-recommendations-and-bug-reports-reports) (7)
- [Event topics](#event-topics)
- [Error codes](#error-codes)

## Endpoint built-ins (mechanica.*)

### `mechanica.auth`

*method, observe scope*

Gives the endpoint's token. Needed first when the host set one; grants that token's scopes.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `token` | string | yes |  |  |

### `mechanica.describe`

*method, observe scope*

Every method (Action API actions and queries, built-ins and module methods) with its scope, description and a JSON Schema of its params, plus the event topics. Pass names to describe only those.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `names` | string[] or null |  |  |  |

### `mechanica.info`

*method, observe scope*

Game name, protocol version, tick rate, the current tick, this connection's scopes and caller, and the names of every action and query.

No params.

### `mechanica.join`

*method, play scope*

Joins the world as a player of your own, marked as an AI. From then on this connection's actions act as that player (its inventory, reach and quests), and SetPlayerInput moves it. Returns its peer, profile id and shown name.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `name` | string | yes |  | The agent's name. Its saved player (inventory, health, position) follows the name, so use the same one to come back. |

### `mechanica.leave`

*method, play scope*

Removes the player this connection joined as (it is saved) and goes back to acting as an external agent on the host.

No params.

### `mechanica.record.start`

*method, observe scope*

Starts recording every state-changing Action API call on the host.

No params.

### `mechanica.record.stop`

*method, observe scope*

Stops recording and returns the recording.

No params.

### `mechanica.replay.start`

*method, admin scope*

Replays a recording through the validator as this connection, with the recorded tick spacing unless timed is false. Returns the number of calls it will replay.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `recording` | any | yes |  |  |
| `timed` | boolean |  | `true` |  |

### `mechanica.replay.status`

*method, observe scope*

Progress of the current or last replay: running, dispatched, total and mismatches.

No params.

### `mechanica.replay.stop`

*method, admin scope*

Stops a replay in progress.

No params.

### `mechanica.subscribe`

*method, observe scope*

Starts pushing mechanica.event notifications for the given topics (see mechanica.describe's topics). Returns the topics now subscribed.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `topics` | string[] or null |  |  |  |

### `mechanica.unsubscribe`

*method, observe scope*

Stops the given topics, or every topic when none are given. Returns the topics still subscribed.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `topics` | string[] or null |  |  |  |

## Agent players

### `SetPlayerInput`

*action, play scope*

Moves an agent's own player and turns its view. Arguments of SetPlayerInput: what an agent's player does for the next Ticks physics ticks, as if its keys were held that long. A new call replaces the one still running.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `move_x` | number |  | `0` | Strafe, -1 (left) to 1 (right). |
| `move_z` | number |  | `0` | Forward, -1 (back) to 1 (forward), relative to where the player looks. |
| `yaw` | number or null |  |  | Look yaw in radians (0 looks down -Z, positive turns left); left out keeps the current one. |
| `pitch` | number or null |  |  | Look pitch in radians (positive looks up); left out keeps the current one. |
| `jump` | boolean |  | `false` | Hold jump. |
| `sprint` | boolean |  | `false` | Hold sprint. |
| `crawl` | boolean |  | `false` | Hold crouch (dives in deep water). |
| `ticks` | integer |  | `1` | How many physics ticks to hold this for, 1 to the host's max_input_ticks; then the player stands still. |

### `SetThought`

*action, play scope*

Shows what an agent's own player is doing or thinking to the people watching it (the spectator camera and its name tag). Arguments of SetThought.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `text` | string |  | `""` | What the agent is doing or thinking now, in a sentence or two; empty clears it. Longer text is cut to the host's max_thought_chars. |

## Seeing the world and the catalog

### `GetDrops`

*query, observe scope*

Item piles on the ground. Arguments of GetDrops.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `center` | number[3] or null |  |  | World point to search around; null is the caller's feet (or every pile with no radius). |
| `radius` | number or null |  |  | Search radius in m; null is every pile. |

### `GetObjects`

*query, observe scope*

World objects near a point. Arguments of GetObjects.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `center` | number[3] or null |  |  | World point to search around; null is the caller's feet. |
| `radius` | number |  | `32` | Search radius in m, up to MaxObjectRadius. |
| `type` | string or null |  |  | Only objects of this type (tree, rock_node, ...). |
| `limit` | integer |  | `100` | Most objects returned, nearest first. |

### `GetPart`

*query, observe scope*

One part or item. Arguments of GetPart.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `part` | string | yes |  | Qualified id (a bare id means vanilla). |

### `GetPlayer`

*query, observe scope*

One player. Arguments of GetPlayer.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `peer` | integer or null |  |  | The player's peer; null is the caller's own player. |

### `GetPlayers`

*query, observe scope*

Every player.

No params.

### `GetPois`

*query, observe scope*

POIs. Arguments of GetPois.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `all` | boolean |  | `false` | Also list POIs the story has not revealed. Players can't; scripts and agents can. |

### `GetQuestBook`

*query, observe scope*

Quests.

No params.

### `GetWorld`

*query, observe scope*

The world's settings and clock.

No params.

### `ListBotTypes`

*query, observe scope*

Bot types.

No params.

### `ListCharacters`

*query, observe scope*

Player characters.

No params.

### `ListItems`

*query, observe scope*

Every item. Arguments of ListParts and ListItems. Every filter is optional.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `category` | one of "block", "interactive", "container", "station", "light", "decor", "resource", "consumable", "tool", "weapon" or null |  |  | Only this category. |
| `mod` | string or null |  |  | Only parts whose qualified id is in this mod. |
| `search` | string or null |  |  | Only ids or names that contain this text (any case). |

### `ListParts`

*query, observe scope*

Buildable parts. Arguments of ListParts and ListItems. Every filter is optional.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `category` | one of "block", "interactive", "container", "station", "light", "decor", "resource", "consumable", "tool", "weapon" or null |  |  | Only this category. |
| `mod` | string or null |  |  | Only parts whose qualified id is in this mod. |
| `search` | string or null |  |  | Only ids or names that contain this text (any case). |

### `ListRecipes`

*query, observe scope*

Recipes. Arguments of ListRecipes. Every filter is optional.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `station` | string or null |  |  | Only recipes this station part runs. |
| `makes` | string or null |  |  | Only recipes that make this item. |
| `uses` | string or null |  |  | Only recipes that use up this item. |

### `Raycast`

*query, observe scope*

Cast a ray. Arguments of Raycast. With no arguments it is "what am I looking at".

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `from` | number[3] or null |  |  | World start point, in m; null is the caller's eyes. |
| `direction` | number[3] or null |  |  | Direction (any length but 0); null is where the caller looks. |
| `range` | number |  | `100` | How far to look, in m, up to MaxRayRange. |
| `ignore_player` | integer or null |  |  | A player whose body the ray passes through; null is the caller's own when the ray starts at their eyes, else none. |

### `SampleTerrain`

*query, observe scope*

Sample the ground. Arguments of SampleTerrain: either Points, or a grid from Min to Max every Step metres. At most MaxSamples samples.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `points` | number[][] or null |  |  | World [x, z] points. |
| `min` | number[] or null |  |  | Grid corner [x, z]. |
| `max` | number[] or null |  |  | Opposite grid corner [x, z]. |
| `step` | number |  | `1` | Grid spacing in m. |

## Bots and raids

### `BotAttack`

*action, admin scope*

A bot's attack. Arguments of BotAttack. Melee and ranged attacks name exactly one target: a player, a part (a crop's soil counts), another bot or, for an attack with object_damage, a world object. Area attacks need none.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `bot` | integer | yes |  | The attacking bot. |
| `attack` | string | yes |  | Id of one of its attacks. |
| `player` | integer or null |  |  | Peer of the player to hit. |
| `creation` | integer or null |  |  | Creation holding the part to hit. |
| `part` | integer or null |  |  | The part to hit. |
| `target` | integer or null |  |  | The bot to hit. |
| `object` | string or null |  |  | The world object to hit (a tree, rock or bush), by its id. |

### `DespawnBot`

*action, admin scope*

Taking a bot out of the world. Arguments of the actions that name one bot (DespawnBot, GetBot).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `bot` | integer | yes |  | The bot's id. |

### `GetBot`

*query, observe scope*

Reading one bot. Arguments of the actions that name one bot (DespawnBot, GetBot).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `bot` | integer | yes |  | The bot's id. |

### `GetBots`

*query, observe scope*

Listing every bot.

No params.

### `GetRaid`

*query, observe scope*

Reading the raid in progress.

No params.

### `MoveBot`

*action, admin scope*

Setting where a bot walks. Arguments of MoveBot: which way a bot walks, until told otherwise.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `bot` | integer | yes |  | The bot. |
| `direction` | number[3] or null |  |  | Direction to walk in; only its horizontal part counts. Null stops it. |
| `speed` | number or null |  |  | Speed in m/s, at most its top speed. Null means top speed. |

### `SpawnBot`

*action, admin scope*

Putting a bot in the world. Arguments of SpawnBot: put a bot in the world.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `bot` | string | yes |  | Bot type id (mod_id:bot_id; a bare id means vanilla). |
| `position` | number[] | yes |  | Where its feet go, [x, y, z] metres. |
| `yaw` | number |  | `0` | Which way it faces, in degrees about the vertical. |

### `StartRaid`

*action, admin scope*

Starting a raid now. Arguments of StartRaid.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `level` | integer or null |  |  | Raid level to send; null picks it from the crops, as a raid check would. |

## Building

### `Block`

*action, play scope*

Starting or stopping a block. Arguments of Block: the caller starts or stops blocking with the Sledgehammer.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `on` | boolean | yes |  | True while right click is held. |

### `LoadBlueprint`

*action, play scope*

Loading a blueprint onto the caller's Lift. Arguments of LoadBlueprint: a blueprint file's JSON (Blueprint), loaded onto the caller's empty Lift.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `blueprint` | any | yes |  | The blueprint. |

### `Paint`

*action, play scope*

Painting parts. Arguments of Paint.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | The Creation holding the parts. |
| `parts` | integer[] | yes |  | Part ids to paint, without repeats. |
| `color` | string or null |  |  | "#rrggbb", or null to take the paint off. |

### `PlaceLift`

*action, play scope*

Placing, moving or loading the caller's Lift. Arguments of PlaceLift.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `position` | number[3] | yes |  | Where the middle of the platform's base goes, in world metres; x and z snap to the grid. Format: [x, y, z] |
| `creation` | integer or null |  |  | A Creation to pick up onto the Lift. Without it, a Lift that already holds one carries it along. |

### `PlacePart`

*action, play scope*

Placing. Arguments of PlacePart. Give Creation to attach to an existing Creation, with Cell in its grid frame; or give Position to start a new Creation whose grid frame sits at that world position, aligned with the world axes.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `part` | string | yes |  | Part id, mod_id:part_id (a bare id means vanilla). |
| `cell` | integer[3] | yes |  | Grid cell of the part's local origin cell. Format: Grid cell [x, y, z] (0.25 m cells). |
| `rotation` | string matching ^[+-][xyz][+-][xyz]$ |  |  | Orientation on the grid, like "+x+y". Format: Grid orientation: the directions the part's local +x and +y point, like "+x+y". |
| `creation` | integer or null |  |  | Creation to attach to. |
| `position` | number[3] or null |  |  | World position in metres of a new Creation's grid origin. |

### `PlaceParts`

*action, play scope*

Placing a drag's worth of parts. Arguments of PlaceParts: one part at each of Cells, all in one grid frame (a Hammer drag, UI spec §2). With Position the first cell starts a new Creation there and the rest attach to it.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `part` | string | yes |  | Part id, mod_id:part_id. |
| `cells` | integer[3][] | yes |  | Origin cells, each touching one before it. |
| `rotation` | string matching ^[+-][xyz][+-][xyz]$ |  |  | Orientation of every part. Format: Grid orientation: the directions the part's local +x and +y point, like "+x+y". |
| `creation` | integer or null |  |  | Creation to attach to. |
| `position` | number[3] or null |  |  | World position in metres of a new Creation's grid origin. |

### `RemoveLift`

*action, play scope*

Taking the caller's Lift away (its Creation drops).

No params.

### `RemovePart`

*action, play scope*

Removing. Arguments of RemovePart.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation holding the part. |
| `part` | integer | yes |  | Part id within that Creation. |

### `RemoveParts`

*action, play scope*

Removing a drag's worth of parts. Arguments of RemoveParts: parts of one Body of a Creation (a Hammer drag, UI spec §8).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation holding the parts. |
| `parts` | integer[] | yes |  | Part ids, all on the same Body. |

### `SetLiftLevel`

*action, play scope*

Raising or lowering the caller's Lift. Arguments of SetLiftLevel.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `level` | integer | yes |  | The new level, 0 to MaxLevel. |

### `Sledge`

*action, play scope*

A Sledgehammer hit. Arguments of Sledge: one Sledgehammer hit on a part (Creation and Part), a player (Player), a world object such as a tree (Object), a bot (Bot), or, with only At, anything else it lands on (the ground, a rock face): that hit only thuds.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer or null |  |  | The Creation hit. |
| `part` | integer or null |  |  | The part hit. |
| `player` | integer or null |  |  | Peer of the player hit. |
| `object` | string or null |  |  | Id of the world object hit ("3.10.-4"). |
| `bot` | integer or null |  |  | Id of the bot hit. |
| `at` | number[3] or null |  |  | Where the head lands, in world metres: the hit's sound and dust play there. |

### `TurnLift`

*action, play scope*

Turning the Creation on the caller's Lift. Arguments of TurnLift.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `turns` | integer | yes |  | Quarter turns about the vertical, counter-clockwise seen from above (negative turns the other way). |

### `Weld`

*action, play scope*

Welding one Creation onto another. Arguments of Weld: move the whole Source Creation onto Target, mapping its grid by Rotation then Offset into the target's grid, and merge the two with every touching face rigid.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `source` | integer | yes |  | The Creation that moves. |
| `target` | integer | yes |  | The Creation it is welded to. |
| `offset` | integer[3] | yes |  | Where the source's grid origin lands in the target's grid. Format: Grid cell [x, y, z] (0.25 m cells). |
| `rotation` | string matching ^[+-][xyz][+-][xyz]$ |  |  | How the source's grid turns into the target's. Format: Grid orientation: the directions the part's local +x and +y point, like "+x+y". |

### `WeldParts`

*action, play scope*

Welding two Bodies of one Creation. Arguments of WeldParts: two touching parts of one Creation, in different Bodies.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | The Creation holding both. |
| `a` | integer | yes |  | The part selected first. |
| `b` | integer | yes |  | The part it is welded to. |

## Characters

### `ChooseCharacter`

*action, play scope*

Picks the caller's character for this world. Only once per player per world. Arguments of ChooseCharacter.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `character` | string | yes |  | Character id (a bare id means vanilla). |

## Chat

### `SendChat`

*action, play scope*

Say a line. Arguments of SendChat.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `text` | string | yes |  | What to say, 1 to MaxLength characters. |

## Debug and general

### `ClearCreations`

*action, admin scope*

Remove every Creation.

No params.

### `SpawnPart`

*action, admin scope*

Spawn a part as a new Creation. Arguments of SpawnPart: a part to drop into the world as a new one-part Creation.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `part` | string | yes |  | Qualified part id (mod_id:part_id; a bare id means vanilla). |
| `position` | number[] | yes |  | World position of the part's footprint centre, [x, y, z] metres. |
| `yaw` | number |  | `0` | Turn about the vertical axis in degrees. |

## Fire

### `GetFires`

*query, observe scope*

Reading every fire.

No params.

## Items, inventories and crafting

### `CancelCraft`

*action, play scope*

Cancelling an order. Arguments of CancelCraft: drop an order and get its unfinished crafts' inputs back.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation holding the station. |
| `part` | integer | yes |  | The station part. |
| `job` | integer | yes |  | Index of the order in the station's queue (0 is the one being worked). |

### `Craft`

*action, play scope*

Queueing a craft. Arguments of Craft: queue Count crafts of a recipe at a station part.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation holding the station. |
| `part` | integer | yes |  | The station part. |
| `recipe` | string | yes |  | Recipe id (id means vanilla, or mod_id:id). |
| `count` | integer |  | `1` | How many crafts (1 or more). |

### `GetHotbar`

*query, observe scope*

Reads the caller's hotbar (HotbarSlots).

No params.

### `GetInventory`

*query, observe scope*

Reading an inventory. Arguments of GetInventory.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `inventory` | object | yes |  | The inventory to read. |
| `inventory.player` | integer or null |  |  | Peer id of the player whose inventory it is. |
| `inventory.creation` | integer or null |  |  | Creation holding the part. |
| `inventory.part` | integer or null |  |  | Part id inside that Creation. |

### `GiveItems`

*action, admin scope*

Making items (Creative). Arguments of GiveItems (Creative only).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `item` | string | yes |  | Item id. |
| `count` | integer | yes |  | How many (1 or more). |
| `to` | object or null |  |  | Where they go; null means the caller's own inventory. |
| `to.player` | integer or null |  |  | Peer id of the player whose inventory it is. |
| `to.creation` | integer or null |  |  | Creation holding the part. |
| `to.part` | integer or null |  |  | Part id inside that Creation. |

### `MoveItems`

*action, play scope*

Moving items. Arguments of MoveItems.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `from` | object | yes |  | Where the items are. |
| `from.player` | integer or null |  |  | Peer id of the player whose inventory it is. |
| `from.creation` | integer or null |  |  | Creation holding the part. |
| `from.part` | integer or null |  |  | Part id inside that Creation. |
| `from_slot` | integer | yes |  | The slot they are in. |
| `to` | object | yes |  | Where they go (may be the same inventory). |
| `to.player` | integer or null |  |  | Peer id of the player whose inventory it is. |
| `to.creation` | integer or null |  |  | Creation holding the part. |
| `to.part` | integer or null |  |  | Part id inside that Creation. |
| `to_slot` | integer or null |  |  | The slot they go to; null puts them wherever they fit. |
| `count` | integer or null |  |  | How many; null moves the whole stack. |

### `SetHotbarSlot`

*action, play scope*

Puts a tool or an item in one of the caller's slots, or empties it. Arguments of SetHotbarSlot: what a slot holds.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `slot` | integer | yes |  | Slot, 0 to 9 (keys 1 to 9 and 0). |
| `entry` | string or null |  |  | A tool (HotbarTools), an item id, or null to empty the slot. |

### `UpgradePart`

*action, play scope*

Upgrading a part. Arguments of UpgradePart: raise a placed part one upgrade level.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation holding the part. |
| `part` | integer | yes |  | The part. |

## Logic, seats and part menus

### `ActivatePart`

*action, play scope*

Use a part. Arguments of ActivatePart and EnterSeat: one part of a Creation.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | The Creation holding the part. |
| `part` | integer | yes |  | The part. |

### `CommandPart`

*action, play scope*

Send a part's script a command from its menu. Arguments of CommandPart: a command from a part's menu to its script's on_command.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | The Creation holding the part. |
| `part` | integer | yes |  | The part. |
| `command` | string | yes |  | The command, one the part's data offers (a sequence setting's Preview). |
| `args` | any or null |  |  | Plain data for the script. |

### `ConfigurePart`

*action, play scope*

Change a part's settings. Arguments of ConfigurePart: settings to change on one placed part.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | The Creation holding the part. |
| `part` | integer | yes |  | The part. |
| `settings` | map of any or null | yes |  | New values by setting id; null puts a setting back to its default. Settings not named keep their value. |

### `ConnectParts`

*action, play scope*

Add a logic link. Arguments of ConnectParts and DisconnectParts: a logic link inside one Creation.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | The Creation holding both parts. |
| `from` | integer | yes |  | The part whose output the link carries. |
| `to` | integer | yes |  | The part it feeds. |

### `DisconnectParts`

*action, play scope*

Remove a logic link. Arguments of ConnectParts and DisconnectParts: a logic link inside one Creation.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | The Creation holding both parts. |
| `from` | integer | yes |  | The part whose output the link carries. |
| `to` | integer | yes |  | The part it feeds. |

### `EnterSeat`

*action, play scope*

Sit in a seat. Arguments of ActivatePart and EnterSeat: one part of a Creation.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | The Creation holding the part. |
| `part` | integer | yes |  | The part. |

### `InspectCreation`

*query, observe scope*

Describe a Creation (read-only). Arguments of InspectCreation.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | The Creation to describe. |

### `LeaveSeat`

*action, play scope*

Stand up from the caller's seat.

No params.

## Map markers

### `AddMapMarker`

*action, play scope*

Marks a place on the caller's map. Arguments of AddMapMarker: where, and an optional name (a numbered "Marker N" without one).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `x` | number | yes |  |  |
| `y` | number | yes |  |  |
| `z` | number | yes |  |  |
| `name` | string or null |  |  |  |

### `GetMapMarkers`

*query, observe scope*

Reads the caller's markers.

No params.

### `RemoveMapMarker`

*action, play scope*

Removes one of the caller's markers. Arguments of RemoveMapMarker.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `id` | string | yes |  |  |

## Motion and physics primitives

### `ApplyForce`

*action, play scope*

Pushing a Body. Arguments of ApplyForce.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation to push. |
| `force` | number[3] | yes |  | World-space force in N, through the Body's centre of mass. Format: [x, y, z] |
| `body` | integer |  | `0` | Index of the Body in Bodies. |
| `torque` | number[3] or null |  |  | World-space torque in N·m. |
| `duration` | number or null |  |  | How long to keep pushing, in s. Null means one physics step. A later call on the same Body replaces it. |
| `at` | number[3] or null |  |  | World point the force acts at, in m. Null pushes through the centre of mass; any other point also twists the Body. |
| `part` | integer or null |  |  | The part doing the pushing (a thruster, a wing). Pushes from different parts of one Body add up, and a later call from the same part replaces only that part's push. The part must be in Body. Null is one shared push per Body, as above. |

### `GetCreation`

*query, observe scope*

Reading one Creation. Arguments of GetCreation.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation to describe. |

### `GetForces`

*query, observe scope*

Reading the pushes on a Creation (the aero force overlay's query). Arguments of GetForces.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation whose pushes to read. |

### `ListCreations`

*query, observe scope*

Listing Creations.

No params.

### `SetBearingTarget`

*action, play scope*

Driving a bearing. Arguments of SetBearingTarget.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation holding the bearing. |
| `part` | integer | yes |  | The bearing's part id (the part that owns the rotational joint). |
| `velocity` | number or null |  |  | Target speed in rad/s about the bearing's axis. Without Angle, null turns the motor off so the bearing spins freely. With an angle it is the most speed the bearing turns there at (either sign); null uses the default from data. |
| `max_torque` | number or null |  |  | Motor torque limit in N·m; null uses the default from data. |
| `angle` | number or null |  |  | Angle in rad to turn to and hold, measured from the pose the bearing was built in (steering, doors). Within ±π, and within the joint's limits when it has them. Null drives at Velocity. |

### `SetPistonTarget`

*action, play scope*

Driving a piston. Arguments of SetPistonTarget.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation holding the piston. |
| `part` | integer | yes |  | The piston's part id (the part that owns the linear joint). |
| `length` | number or null | yes |  | Extension in m to move to and hold, within the joint's min and max. Null turns the motor off so the piston slides freely. |
| `speed` | number or null |  |  | Most speed in m/s; null uses the default from data. |
| `max_force` | number or null |  |  | Motor force limit in N; null uses the default from data. |

## Quests

### `ChooseQuestOption`

*action, play scope*

Picks a choice of a choose step. Arguments of ChooseQuestOption.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `quest` | string | yes |  | Quest id. |
| `choice` | string | yes |  | Choice id of its current step. |

### `GetQuests`

*query, observe scope*

Reads the quest state.

No params.

### `QuestInteract`

*action, play scope*

Uses the nearest quest marker in reach.

No params.

## Survival

### `CancelRefine`

*action, play scope*

Letting go of Use (or moving off) before a refine finished.

No params.

### `CollectWater`

*action, play scope*

Filling a bucket at water. Arguments of CollectWater: fill an empty bucket where the caller aims at water.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `point` | number[3] | yes |  | The water point aimed at, in world metres. Format: [x, y, z] |
| `slot` | integer or null |  |  | The inventory slot holding the empty bucket to fill (the one in hand); null fills the first one. |

### `DropItems`

*action, play scope*

Putting items on the ground. Arguments of DropItems: put items from a slot on the ground in front of the caller.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `slot` | integer | yes |  | The slot. |
| `count` | integer or null |  |  | How many; null drops the whole stack. |
| `at` | number[3] or null |  |  | Force Build (UI spec §1, §9): the world point the caller aims at, within reach, where the items are placed and stay (they never expire). Null drops them in front of the caller. |

### `FinishRefine`

*action, play scope*

Finishing a refine once Use has been held long enough. Arguments of StartRefine and FinishRefine: what Use is held on. Either a resource node (a tree) by Object, or a loose part that refines (a Stone Rod) by Creation and Part.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `object` | string or null |  |  | Id of the world object ("3.10.-4"). |
| `creation` | integer or null |  |  | Id of the loose part's Creation. |
| `part` | integer or null |  |  | Id of the loose part in it. |

### `GetSoil`

*query, observe scope*

Reading what grows on a soil part. Arguments of GetSoil.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation holding the soil part. |
| `part` | integer | yes |  | The soil part. |

### `GetVitals`

*query, observe scope*

Reading the caller's health.

No params.

### `PickUp`

*action, play scope*

Picking items up. Arguments of PickUp: take a pile of items off the ground.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `drop` | integer | yes |  | The pile's id. |

### `Respawn`

*action, play scope*

Respawning (dead players), or going back to the spawn spot (living ones).

No params.

### `SetFriendlyFire`

*action, admin scope*

Turning friendly fire on or off (the host only). Arguments of SetFriendlyFire: let players hurt each other in this world, or stop them. The host only.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `on` | boolean | yes |  | True to let players hurt each other. |

### `SetSpawn`

*action, play scope*

Setting the bed to respawn at. Arguments of SetSpawn: one part of a Creation.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `creation` | integer | yes |  | Creation holding the part. |
| `part` | integer | yes |  | The bed part. |
| `slot` | integer |  | `0` | Unused; kept so older callers that send it still parse. |

### `StartRefine`

*action, play scope*

Starting to hold Use on a resource node to refine it. Arguments of StartRefine and FinishRefine: what Use is held on. Either a resource node (a tree) by Object, or a loose part that refines (a Stone Rod) by Creation and Part.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `object` | string or null |  |  | Id of the world object ("3.10.-4"). |
| `creation` | integer or null |  |  | Id of the loose part's Creation. |
| `part` | integer or null |  |  | Id of the loose part in it. |

## Using held items

### `AimItem`

*action, play scope*

Starting or stopping aiming the item in hand (AimItemArgs). Arguments of AimItem: the caller starts or stops aiming the item in hand (holding right click; UI spec §10).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `on` | boolean | yes |  | True while right click is held. |

### `EquipItem`

*action, play scope*

Taking an item in hand, or emptying the hand (EquipItemArgs). Arguments of EquipItem: take the item in a slot in hand, or empty the hand.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `slot` | integer or null |  |  | The slot holding the item; omitted (null) empties the hand. |

### `ReloadItem`

*action, play scope*

Reloading the item in a slot (ReloadItemArgs). Arguments of ReloadItem: reload the item in a slot (its script's on_reload_item; UI spec §1, §10).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `slot` | integer | yes |  | The slot holding the item. |

### `UseItem`

*action, play scope*

Using the item in a slot (UseItemArgs). Arguments of UseItem: use the item in a slot of the caller's inventory (its script's on_use).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `slot` | integer | yes |  | The slot holding the item. |
| `direction` | number[3] or null |  |  | Where the caller aims from their eyes; the host checks it against where they look. Omitted means where they look. |
| `creation` | integer or null |  |  | The Creation of the part the item is used on (soil for a seed), if any. Give it with Part. |
| `part` | integer or null |  |  | The part the item is used on, if any. The script checks reach (farm.soil does). |

## World

### `world.friendly_fire`

*method, admin scope*

Let players hurt each other (on: true) or stop them (on: false). With it off no player, AI or player-built machine hurts a player; bots still do. Every player is told. Returns the status.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `on` | boolean | yes |  | True to let players hurt each other, false to stop them. |

### `world.info`

*method, admin scope*

The running world: name, seed, mode, difficulty, friendly fire, the save it came from, world time, tick, time scale, paused, joined players, and whether a world change is under way.

No params.

### `world.list`

*method, admin scope*

Saved worlds, newest first: name, file, saved_at, mode, play_seconds, and error for a save that does not load.

No params.

### `world.load`

*method, admin scope*

Load a saved world by name or file name. Answers at once; the world loads after, and the connection carries over. Players joined are disconnected, so it needs disconnect_clients when there are any.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `name` | string | yes |  | A saved world's name or file name, as world.list shows it. |
| `disconnect_clients` | boolean |  | `false` | True to go ahead although players are joined; they are disconnected and can join the new world. |

### `world.new`

*method, admin scope*

Start a new world (name, seed, mode, difficulty, character, friendly_fire, off by default). Answers at once with what it will be; the world starts after.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `name` | string |  | `"World"` | The new world's name. |
| `seed` | integer or null |  |  | World seed; random when left out. |
| `mode` | one of "creative", "survival" |  | `"survival"` | Game mode. |
| `difficulty` | one of "easy", "normal" |  | `"normal"` | Difficulty. |
| `character` | string or null |  |  | The host player's character (qualified id). |
| `disconnect_clients` | boolean |  | `false` | True to go ahead although players are joined. |
| `friendly_fire` | boolean |  | `false` | True to let players hurt each other (off by default). |

### `world.pause`

*method, admin scope*

Pause or resume the world. The endpoint keeps answering while paused. Returns the status.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `paused` | boolean | yes |  | True to pause, false to resume. |

### `world.reset`

*method, admin scope*

Start the running world again from nothing: the same name, seed, mode, difficulty and friendly fire, nothing built. For a clean test world.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `disconnect_clients` | boolean |  | `false` | True to go ahead although players are joined. |

### `world.save`

*method, admin scope*

Save the world, under its own name or a new one. Returns the name, file and path.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `name` | string or null |  |  | Name to save as; the world's own name when left out. |

### `world.time_scale`

*method, admin scope*

Run the world faster or slower (0.05 to 10, 1 is real time). Returns the status.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `scale` | number | yes |  | How fast the world runs, from MinTimeScale to MaxTimeScale (1 is real time). |

## Editors (editor.*)

### `editor.batch.run`

*method, admin scope*

Runs a batch file of a mod (Part Editor parts and recipes, Bot Behavior Editor bots and raids, or story POIs and quests) as tools/author-vanilla.sh does; this is how vanilla content is changed. Returns kind, ok, every export and the diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `path` | string | yes |  | Forward-slash path inside the mod. |

### `editor.bot.edit`

*method, admin scope*

One undo step: merges set into the bot (stats, attacks, drops, script; null removes a key). Returns the document and its diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | Bot document. |
| `set` | any | yes |  | Keys to merge into the bot (null removes a key). |
| `label` | string or null |  |  | Undo label; defaults to "Edit". |

### `editor.bot.export`

*method, admin scope*

Exports the bot into its mod's bots/. Returns ok, id, file and diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | The handle editor.part.new, editor.part.open, editor.bot.new or editor.bot.open returned. |

### `editor.bot.get`

*method, admin scope*

The bot document as it stands: bot, dirty, undo label.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | The handle editor.part.new, editor.part.open, editor.bot.new or editor.bot.open returned. |

### `editor.bot.new`

*method, admin scope*

Starts a Bot Behavior Editor document from a template in a mod. Nothing is written until editor.bot.export. Returns the document (doc handle and the bot data, behaviour tree included) and its diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod the new document belongs to. |
| `template` | string | yes |  | Template name, from editor.templates. |
| `id` | string | yes |  | New id, lowercase snake_case. |
| `name` | string or null |  |  | Display name; defaults to the id. |

### `editor.bot.open`

*method, admin scope*

Opens a bot file of a mod (bots/…). Returns the document and its diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `path` | string | yes |  | Forward-slash path inside the mod. |

### `editor.bot.script`

*method, admin scope*

The bot's own Lua script, or null when it has none in its mod.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | The handle editor.part.new, editor.part.open, editor.bot.new or editor.bot.open returned. |

### `editor.bot.script_save`

*method, admin scope*

Compiles the bot's own script, writes it into the mod's scripts/ and points the bot at it (export the bot to keep that). Refused when it doesn't compile. Returns the script path and the bot's diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | Bot document. |
| `code` | string | yes |  | The script. |

### `editor.bot.tree`

*method, admin scope*

One behaviour tree edit at a node path (child indexes from the root): add a node of a type under it, remove it, move it by delta among its siblings, or set a parameter (name, value; null removes it). Returns the affected node's path, the document and its diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | Bot document. |
| `op` | string | yes |  | add, remove, move or set. |
| `path` | integer[] | yes |  | Child indexes from the root of the behaviour tree; [] is the root. |
| `type` | string or null |  |  | add: the node type. |
| `delta` | integer or null |  |  | move: places to move among its siblings (negative is earlier). |
| `name` | string or null |  |  | set: the parameter. |
| `value` | any or null |  |  | set: its value; null removes it. |

### `editor.bot.undo`

*method, admin scope*

Undoes one step (or redoes, with redo). Returns the document and its diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | Document. |
| `redo` | boolean |  | `false` | True redoes instead. |

### `editor.bot.validate`

*method, admin scope*

Checks the bot exactly as loading would, against the parts on disk now.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | The handle editor.part.new, editor.part.open, editor.bot.new or editor.bot.open returned. |

### `editor.close`

*method, admin scope*

Closes a part or bot document; unsaved changes are dropped. At most 64 documents are open at once.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | The handle editor.part.new, editor.part.open, editor.bot.new or editor.bot.open returned. |

### `editor.part.contacts`

*method, admin scope*

The face contacts the part makes, as the socket preview shows them: cell, direction, connection, socket.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | The handle editor.part.new, editor.part.open, editor.bot.new or editor.bot.open returned. |

### `editor.part.edit`

*method, admin scope*

One undo step: replace the definition, or merge set into it (null removes a key, objects merge), and/or replace the model pieces (source). Returns the document and its diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | Part document. |
| `label` | string or null |  |  | Undo label; defaults to "Edit". |
| `definition` | any or null |  |  | The whole part definition, replacing it. |
| `set` | any or null |  |  | Or: keys to merge into the definition (null removes a key, objects merge). |
| `source` | any or null |  |  | The model pieces, replacing them. |

### `editor.part.export`

*method, admin scope*

Exports the part into its mod: the part file, its model pieces under editor/parts/, and a mesh baked by the editor's model builder when the pieces need one. Returns ok, id, file and diagnostics; mods.reload loads it.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | The handle editor.part.new, editor.part.open, editor.bot.new or editor.bot.open returned. |

### `editor.part.get`

*method, admin scope*

The part document as it stands: definition, source (model pieces), dirty, undo and redo labels, needs_bake.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | The handle editor.part.new, editor.part.open, editor.bot.new or editor.bot.open returned. |

### `editor.part.new`

*method, admin scope*

Starts a Part Editor document from a template (block, wedge, bearing, …) in a mod. Nothing is written until editor.part.export. Returns the document (doc handle, definition, model pieces as source) and its diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod the new document belongs to. |
| `template` | string | yes |  | Template name, from editor.templates. |
| `id` | string | yes |  | New id, lowercase snake_case. |
| `name` | string or null |  |  | Display name; defaults to the id. |

### `editor.part.open`

*method, admin scope*

Opens a part file of a mod (parts/… or overrides/…) with its model pieces. Returns the document and its diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `path` | string | yes |  | Forward-slash path inside the mod. |

### `editor.part.sound`

*method, admin scope*

Binds one of the part's sound names to a sound event id, or unbinds it with a null sound. Returns the document and its diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | Part document. |
| `key` | string | yes |  | The part's sound name, like place or impact. |
| `sound` | string or null |  |  | Sound event id, or null to unbind it. |

### `editor.part.undo`

*method, admin scope*

Undoes one step (or redoes, with redo). Returns the document and its diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | Document. |
| `redo` | boolean |  | `false` | True redoes instead. |

### `editor.part.validate`

*method, admin scope*

Checks the part and its model pieces exactly as export would.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `doc` | string | yes |  | The handle editor.part.new, editor.part.open, editor.bot.new or editor.bot.open returned. |

### `editor.raids.get`

*method, admin scope*

A mod's raid table (raids/raids.json), or null when it has none.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |

### `editor.raids.save`

*method, admin scope*

Checks a raid table against the bots on disk and writes it to the mod's raids/raids.json. Returns ok, file and diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `raids` | any | yes |  | The raid table (the raids/raids.json schema). |

### `editor.recipes.delete`

*method, admin scope*

Deletes a recipe file of a mod.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `path` | string | yes |  | Forward-slash path inside the mod. |

### `editor.recipes.list`

*method, admin scope*

Every recipe file of a mod, with its recipe.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |

### `editor.recipes.save`

*method, admin scope*

Checks and writes a recipe as the Part Editor's Recipes tab does (previous_file is removed when the id changed). Returns ok, id, file and diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `recipe` | any | yes |  | The recipe (the recipes/*.json schema). |
| `previous_file` | string or null |  |  | The file it was listed from, removed when its id changed. |

### `editor.sounds.get`

*method, admin scope*

The events of a sound bank (the mod's default bank when path is null); empty when it is new.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `path` | string or null |  |  | Sound bank file under sounds/; null is the mod's default bank. |

### `editor.sounds.list`

*method, admin scope*

A mod's sound event files.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |

### `editor.sounds.save`

*method, admin scope*

Replaces a sound bank's events, checks them as loading would and writes the file. Returns ok, id, file and diagnostics.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `events` | any | yes |  | Every event of the bank, replacing them. |
| `path` | string or null |  |  | Sound bank file under sounds/; null is the mod's default bank. |

### `editor.templates`

*method, admin scope*

Template names for editor.part.new and editor.bot.new.

No params.

## Lua tools (lua.*)

### `lua.check`

*method, observe scope*

Compiles a script (source text, or mod and path of a file) the way the host does, without running it. Returns ok, or the line, column and message of the first error.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `source` | string or null |  |  | Script text to compile. |
| `mod` | string or null |  |  | Or: the mod of a script file. |
| `path` | string or null |  |  | And the file's path inside it. |

### `lua.eval`

*method, admin scope*

Runs Lua once in a throwaway sandbox with what part scripts start with (safe core libraries and vec3, no io, files or world) under the host's instruction budget. A single expression returns its value. Returns ok, values, printed output and error.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `source` | string | yes |  | Lua to run; a single expression returns its value. |

### `lua.messages`

*method, observe scope*

The newest lines of the part and bot script consoles, oldest first: source (parts or bots), kind (print, warning, error), script, creation, part, text, tick. Subscribe to the messages topic to have them pushed instead.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `since_tick` | integer or null |  |  | Only lines written after this tick. |
| `kinds` | string[] or null |  |  | Only these kinds: print, warning, error. |
| `script` | string or null |  |  | Only this script (mod:scripts/x.lua). |
| `limit` | integer or null |  |  | Newest lines to return, 1 to 500; default 100. |

## Mod packages (package.*)

### `package.export`

*method, admin scope*

Validates an installed mod and exports it to <id>-<version>.zip in the packages folder. A mod with errors is not exported; ok is false and problems say why.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `id` | string | yes |  | Mod id. |

### `package.inspect`

*method, admin scope*

Checks a package in the packages folder without installing it: its manifest, every problem (unsafe paths, hashes, size limits, game version), and what installing it would do (new, upgrade, reinstall, downgrade, replace_broken; dependencies and conflicts).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `file` | string | yes |  | File name in the packages folder, like better_wheels-1.2.0.zip. |

### `package.install`

*method, admin scope*

Installs a package from the packages folder into the mods folder after checking it. Replacing an installed copy needs replace: true. Takes effect with the next world.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `file` | string | yes |  | File name in the packages folder. |
| `replace` | boolean |  | `false` | Must be true to replace an installed copy (upgrade, reinstall, downgrade or a broken folder). |

### `package.list`

*method, admin scope*

Lists the mod packages (.zip) in the packages folder, by file name and size.

No params.

### `package.uninstall`

*method, admin scope*

Deletes an installed mod (not vanilla). dependents lists the installed mods that need it and stop loading. Takes effect with the next world.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `id` | string | yes |  | Mod id. |

### `package.verify`

*method, admin scope*

Compares an installed mod with the package.json it was installed with: files changed, missing or added since (MD1310).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `id` | string | yes |  | Mod id. |

## Mod tools (mods.*)

### `mods.conflicts`

*method, admin scope*

Conflicts between the enabled mods: incompatible (not loaded) and override_clash (two mods override one part; the later wins).

No params.

### `mods.create`

*method, admin scope*

Creates a new mod folder with mod.json and empty parts/ and scripts/. It depends on vanilla unless dependencies says otherwise.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | New mod id, lowercase snake_case; also its folder name. |
| `name` | string or null |  |  | Display name; defaults to the id. |
| `version` | string or null |  |  | Semantic version; defaults to 0.1.0. |
| `description` | string or null |  |  | One paragraph about the mod. |
| `authors` | string[] or null |  |  | Author names. |
| `dependencies` | string[] or null |  |  | Mod ids it needs; defaults to vanilla. |

### `mods.delete`

*method, admin scope*

Deletes a file of a mod, and folders it leaves empty. mod.json can't be deleted.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `path` | string | yes |  | Forward-slash path inside the mod, like parts/wheel.json. |

### `mods.enable`

*method, admin scope*

Enables or disables a mod like the Mod Manager (enabling pulls in its dependencies, disabling its dependents) and saves the profile. Takes effect on mods.reload. Returns the mods that changed.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `enabled` | boolean |  | `true` | False disables it (and the mods that depend on it). |

### `mods.files`

*method, admin scope*

Lists the files of a mod (or one folder of it): path, size and modified time.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `folder` | string or null |  |  | Folder inside the mod; null lists the whole mod. |

### `mods.import`

*method, admin scope*

Imports an asset like the Part Editor: into meshes/, scripts/, textures/ or sounds/ under a clean snake_case name, reusing an identical file. A .gltf brings its buffers and images in companions. Returns the path to write in data.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `kind` | string | yes |  | mesh, script, texture or sound. |
| `file_name` | string | yes |  | The file's name, like Big Wheel.glb; it is cleaned to snake_case. |
| `base64` | string | yes |  | The file, base64. |
| `companions` | map of string or null |  |  | A .gltf's buffers and images by relative path, base64. |

### `mods.list`

*method, admin scope*

Installed mods in load order: id, name, version, enabled, can_disable, loaded, dependencies; plus profile entries that are not installed (missing) and folders whose mod.json is broken.

No params.

### `mods.move`

*method, admin scope*

Moves a mod in the load order (it stops at its dependencies and dependents) and saves the profile. Returns the index it ended up at.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `index` | integer | yes |  | Where in the load order it should go (0 is first); it stops at its dependencies. |

### `mods.read`

*method, admin scope*

Reads a file of a mod: text for .json, .lua, .gltf, .md and .txt, base64 for the rest. Up to 2 MiB.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `path` | string | yes |  | Forward-slash path inside the mod, like parts/wheel.json. |

### `mods.reload`

*method, admin scope*

Reloads the enabled mods into the running world: parts, sounds, part scripts and bot types. World generation, recipes, raids, quests and characters change only in a new world (listed in next_world). Refused while clients are joined unless the host runs with --ai-allow-reload-with-clients, which drops clients whose mods no longer match (dropped_peers).

No params.

### `mods.validate`

*method, admin scope*

Loads all content as a world would (parts, sounds, world generation, recipes, bots, raids, quests, characters) and compiles every Lua script. Returns ok, error and warning counts, the mods that would load, the content fingerprint and every diagnostic (domain, severity, code, mod, file, json_path, line, message). A mod with part errors is left out, so its recipes and bots are checked once its parts load.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string or null |  |  | Only this mod's problems (it is enabled for the check); null checks everything enabled. |

### `mods.write`

*method, admin scope*

Writes a file of a mod (text or base64, up to 2 MiB), creating folders. Paths stay inside the mod; vanilla only changes under editor/. Returns the bytes written and a quick check: JSON syntax for .json, a compile for .lua. Run mods.validate for the full check.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `mod` | string | yes |  | Mod id. |
| `path` | string | yes |  | Forward-slash path inside the mod. |
| `text` | string or null |  |  | Content of a text file (.json, .lua, .gltf, .md, .txt). |
| `base64` | string or null |  |  | Content of a binary file (.glb, .bin, .png, .ogg, .wav), base64. |

## Notes, recommendations and bug reports (reports.*)

### `reports.bug`

*method, observe scope*

Save a bug report with steps, expected and actual. The host adds the game's context, the calls since the world started as a replay, recent log lines and a screenshot.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `title` | string | yes |  | One line, at most 200 characters. |
| `steps` | string[] | yes |  | Steps to reproduce, in order. |
| `expected` | string | yes |  | What should have happened. |
| `actual` | string | yes |  | What happened instead. |
| `severity` | one of "low", "medium", "high", "critical" |  | `"medium"` | How bad it is. |
| `text` | string |  | `""` | More detail (Markdown). |
| `area` | string or null |  |  | The feature, tool, part or mod it is about. |
| `tags` | string[] or null |  |  | Tags for filtering. |
| `screenshot` | boolean |  | `true` | True (the default) to attach what the host's screen shows; a headless host notes why it has none. |
| `replay_actions` | integer or null |  |  | Most state-changing calls to keep in the replay file, newest last; null keeps every call since the world started. |
| `messages` | integer |  | `50` | Most recent log lines to keep (0 to 200). |
| `author` | string or null |  |  | Who is writing (a model or agent name); the connection's name when left out. |

### `reports.delete`

*method, admin scope*

Delete a report and its files.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `id` | string | yes |  | The report's id. |

### `reports.get`

*method, observe scope*

Read one report, optionally with its replay recording and screenshot (base64 PNG).

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `id` | string | yes |  | The report's id. |
| `replay` | boolean |  | `false` | True to include a bug's replay slice. |
| `screenshot` | boolean |  | `false` | True to include the screenshot as base64 PNG. |

### `reports.list`

*method, observe scope*

List saved reports, newest first, filtered by kind, session and tag.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `kind` | one of "note", "recommendation", "bug" or null |  |  | Only this kind. |
| `session` | string or null |  |  | Only this session (a folder name, or current); every session when left out. |
| `tag` | string or null |  |  | Only reports with this tag. |
| `limit` | integer |  | `50` | Most reports returned, newest first (1 to 500). |

### `reports.note`

*method, observe scope*

Save a note: something noticed or worth remembering. Returns the saved report.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `title` | string | yes |  | One line, at most 200 characters. |
| `text` | string |  | `""` | The note (Markdown). |
| `tags` | string[] or null |  |  | Tags for filtering: lower case letters, digits, - and _. |
| `screenshot` | boolean |  | `false` | True to attach what the host's screen shows. |
| `author` | string or null |  |  | Who is writing (a model or agent name); the connection's name when left out. |

### `reports.recommend`

*method, observe scope*

Save a recommendation: a change to the game or a mod, and why. Returns the saved report.

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `title` | string | yes |  | One line, at most 200 characters. |
| `text` | string | yes |  | What to change and why (Markdown). |
| `area` | string or null |  |  | The feature, tool, part or mod it is about. |
| `priority` | one of "low", "medium", "high" |  | `"medium"` | How much it matters. |
| `tags` | string[] or null |  |  | Tags for filtering. |
| `screenshot` | boolean |  | `false` | True to attach what the host's screen shows. |
| `author` | string or null |  |  | Who is writing (a model or agent name); the connection's name when left out. |

### `reports.screenshot`

*method, observe scope*

What the host's screen shows now, as base64 PNG with its width and height.

No params.

## Event topics

Subscribe with `mechanica.subscribe {"topics": [...]}`; each arrives as a `mechanica.event` notification with `topic`, `tick` and `data`.

| Topic | What it carries |
|-------|-----------------|
| `actions` | Every state-changing Action API call by players, bots and agents (not Lua scripts): peer, source, action, args, ok, code, message, value. |
| `bots` | A bot spawned, was removed or attacked: kind, bot, plus the attack or bot view. |
| `chat` | A chat line: the chat message. |
| `dialogue` | A quest character spoke: quest, speaker, text, choices. |
| `drop` | An item drop appeared, changed or was picked up: kind, drop. |
| `events_dropped` | Sent automatically when a subscriber fell behind and notifications were dropped: count. |
| `fire` | A fire went out: the fire view. |
| `hurt` | A player took damage: peer, amount, cause. |
| `messages` | Lua prints, warnings and errors from part, item and bot scripts, and host errors from agent calls: kind, source, script, creation, part, text. |
| `mods` | Mod content was validated or reloaded. |
| `projectiles` | A projectile was launched or landed. |
| `quest` | Quest progress changed: the GetQuests state. |
| `raid` | A raid started, advanced or ended: the GetRaid view. |
| `reports` | An agent filed a note, recommendation or bug report. |
| `seats` | Someone sat down or stood up: seat, seated. |
| `vitals` | A player's vitals changed (health, death, respawn): the GetVitals view. |
| `world` | The world was saved, or a load, new world or reset finished: kind (saved, loaded), plus the world.info status. |

## Error codes

| `error.code` | Meaning |
|--------------|---------|
| -32700 | not valid JSON |
| -32600 | not a JSON-RPC request, or an empty or oversized batch |
| -32601 | no such method (data.code unknown_action) |
| -32602 | params don't fit the method (data.code bad_args) |
| -32603 | the host failed while handling the call (data.code internal_error) |
| -32001 | the host's validation refused it; message says why (data.code rejected) |
| -32002 | too many calls (data.code rate_limited) |
| -32003 | call mechanica.auth first, or the token was wrong |
| -32004 | can't be done now (nothing recording, no replay, busy) |
| -32005 | this connection lacks the method's scope (data.code forbidden) |
