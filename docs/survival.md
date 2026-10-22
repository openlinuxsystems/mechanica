# Survival

A Survival world (the default; `--creative` or Creative in the main menu's New Game starts Creative) has
one meter, health (D12: no stamina, no hunger). The host decides everything through the Action API,
so it works the same for every player in a multiplayer game.

## Difficulty

New Game picks **Normal** or **Easy** for a Survival world (`--easy` on the command line). The world
keeps it in its save. Easy changes these, as multipliers in `game/data/survival.json` (`difficulties`):

| Key | Easy | What it scales |
|-----|------|----------------|
| `bot_damage` | 0.5 | Damage bots do to players |
| `raid_budget` | 0.6 | How many bots a raid sends |
| `raid_cooldown` | 1.5 | The wait after a raid before the next |

## Friendly fire

Off by default: players can't hurt each other. New Game has a **Friendly fire** box for a Survival world
(`--friendly-fire` on the command line), and the host can turn it on or off later in Options, Gameplay
(clients see it there but can't change it; `SetFriendlyFire {on}` is refused for anyone but the host). The
world keeps it in its save. With it off no weapon, splash or Sledgehammer swing of a player hurts another
player, and neither does an external AI or a player-built machine (part scripts such as the mounted
blaster or the cutter). Bots, falls and fire still hurt players, and a player's own splash still hurts
them. AI agents read it from `GetWorld` and `world.info` and set it with `world.friendly_fire`.

## Playing

| Key | What it does |
|-----|--------------|
| 1 to 9, 0 / scroll | Pick the hotbar slot holding the item; it goes in hand. |
| Left click (item in hand) | Use it (all Lua item scripts): eat food, fire a weapon, plant a seed in the soil you aim at or water it, or throw the water out of a full bucket; hold it with a Fire Extinguisher to spray foam. With an empty water bucket in hand, aiming at water fills it. |
| G (item in hand) | Drop one of the item on the ground. |
| E | Harvest the ripe crop you aim at, sleep in the bed you aim at (you respawn there), or pick up the nearest pile of items. |
| E (dead) | Respawn, once the respawn delay is over. |

- **Health** goes down from weapons, the Sledgehammer, splash, and falls faster than
  `fall.min_speed`. It comes back by eating. At 0 you die: everything you carry falls where
  you stood in the knockout bag, a yellow canvas drawstring bag that your compass (with a bag icon, not a waypoint diamond)
  and maps point to until you pick it up or it expires (tools you crafted, like the Connect Tool and the Weld Tool, stay with you), and you respawn at your bed, or at the world spawn if you have none (or it was broken).
  Any part with `respawn_point` is a bed: vanilla has the Respawn Bed, a sleeping bag on a foam mat (it burns), and the
  steel Fancy Bed (it does not, and is tougher). You respawn standing on top of the bed: on its collision box when that
  is a box (the sleeping bag's is lower than its block), else on the top of its size.
- **Food:** left click with food in hand eats it and heals you: a Carrot 25, a Tomato 20 and a Potato 8 (the Potato is
  mostly potato-weapon ammo). Carrots and tomatoes stack to 10, potatoes to 50. Every player starts with 3 carrots.
  The heal is the item's `properties.health`, read by `food.lua`. Eating takes about 1.4 s and stops a sprint.
- **Drinks:** Bloom Tea heals 20 and a Grazer Shake 100. They are found in loot only (chests, lockers, crates and ruin
  chests; the Grazer Shake in the better ones). A drink is downed in two gulps in about 0.7 s, and you can keep sprinting
  while you drink.
- **Grazer Milk and Grazer Steak:** a tame Grazer living by a Feeding Trough eats a carrot from it every 30 s; after
  five it has a bottle of Grazer Milk ready, shown by a milk icon bobbing over its back on every peer. Press Use (E) on
  it to take the bottle into your inventory (you need room); the next is ready after five more carrots and at least
  5 minutes. Calves give none. Grazer Milk is a drink that heals 20 (stack 5). Every Grazer drops one or two Grazer
  Steaks when it dies (stack 5): raw meat that cannot be eaten, kept for cooking. The numbers are the Grazer's
  `tame.produce` and `drops` in `bots/grazer.json` ([bot-tutorial.md](bot-tutorial.md#taming)).
- **Fertilizer:** a Rootwell pouch (Fabricator: 10 Ember and a full Water Bucket make 5 and give the bucket back; also
  found as loot). Use it on a growing crop, in a soil mound or a Growbed, and it grows 15 times faster until it is
  harvested (`properties.boost`); it still grows only while its soil holds water. Fertilized soil shows pale granules.
  The boost is part of the soil's state: saved with its Creation, sent to clients and cleared by the harvest.
- **Ice:** the Freezer is a crafting station with one recipe: a full Water Bucket becomes 20 Ice Blocks in a minute,
  and the empty bucket comes back into the Freezer's slots with the ice.
- **Farming:** aim a Soil Bag at the ground (no steeper than 17°) to spill out a 3×3 soil mound (with Force build held,
  or aimed anywhere but the terrain, the bag itself is set down; picking a mound up gives the bag back). Plant a seed
  and water it. Soil looks dry, lightly watered, fully wet or drying, and empty soil dries out in about a game day. A crop grows only while its soil holds water,
  using a second of water per second of growth, through four stages. Harvest it ripe (E) for its crop
  and usually a seed back. Crops keep growing while nobody is near (the soil catches up from the world
  clock when it next runs) and are saved with the soil's Creation.
- **Growbeds:** the Growbed (Fabricator: 2 Component Kits, 2 Circuit Boards and a Soil Bag) is soil you build into a
  Creation, so a farm can be a cart, a boat or a base. It takes carrot, potato, tomato and cotton seeds, and a harvest
  always gives 5 crops. It runs on batteries: each Battery Pack powers 6 minutes of growth, taken from Battery Containers
  wired into it (or any container on its Creation); its cyan light shows while it grows. The crop rides along with the
  Creation wherever it drives, across chunks and while it is parked, and saves with it. Picking a bed (or a soil mound) up
  gives an unripe crop's seed back; a ripe crop must be harvested first. A bed that breaks drops its seed.
- **Building costs items.** Placing a part takes one from your inventory; removing it gives it back
  (or drops it when your inventory is full). Craft parts at a Portable Fabricator or a Fabricator.
  The Portable Fabricator (every player starts with one) makes the scrap tier from scrap blocks: the Scrap Seat, Scrap
  Driver's Seat, Scrap Gas Engine and Scrap Wheel (made only there), and cheap scrap recipes for the Bearing, Switch and
  Beacon, which the Fabricator makes from Metal Blocks.
- **Item transport:** Vacuum Pipes (and the Vacuum Pipe Corner, to turn) placed end to end join the IO ports of chests,
  containers, stations, Vacuum Pumps and Resource Collectors on a Creation ([IO Ports](io-ports.md#item-transport)). A
  Vacuum Pump with a logic signal (a switch, a sensor) sucks piles, loose resources (logs, rods, chunks), ripe crops and
  water from in front of its mouth into the containers it is piped to; press E on it to set it to Eject, and it throws
  their items out of its mouth instead. The Resource Collector pulls loose resources and piles within 12 blocks of its top
  into itself, and a Refiner piped from it refines what it gathers. Stations take what they need from the containers
  piped to their input port and send what they make down their output port.
- **Breaking things:** a part breaks off at 0 health. A container that breaks spills what it held.
  Trees, rocks and bushes with `health` break into resources (`drops` in `world/worldgen.json`).
  Crashes and falls never damage a Creation: only the Sledgehammer, weapons, bots, explosions and fire do.
- **Explosions:** an Explosive Canister (Small: 1.75 m, Large: 3.75 m) goes off at once when it breaks, when another
  blast reaches it (canisters set each other off in the same moment) or when it hits something at 8 m/s or more. Any
  other blow (a shot, a Sledgehammer, a saw, fire) lights its fuse: it hisses, its lamp blinks red and three seconds
  later it goes off. A blast hurts players, bots, parts and world objects in its reach, most at its middle, pushes
  parts away and sets flammable parts near it burning; every player sees and hears it. A blast that breaks a
  creation apart still reaches the pieces.
- **Piles** of items on the ground last `drop_lifetime` seconds (15 minutes). Drag a stack out of any item window
  (the inventory, a container, the hotbar) and let go over the world to toss it: it flies from your hand and lands
  `toss_distance` ahead as a pile anyone can pick up with E.
  A pile of one item with a model of its own shows that model, scaled up to 0.3 m across when it is smaller (a
  seed packet); any other pile is a small crate in the first item's colour.
- **Fire:** see [Fire](#fire).
- **Raids:** the host checks the planted crops every minute. Once they are worth the first level of the
  raid table (`raids/raids.json`; vanilla's is 10, a carrot or potato counts 1 and a tomato 1.5), the raid
  horn sounds and the level's bots arrive round the biggest patch to go for the crops. Bigger farms and
  more players bring bigger raids; after one there is a cooldown before the next.
- **Guards:** POIs (the warehouse, ruins, outposts) are guarded by the bots their data places. They appear
  when a player comes near, leave when everyone has gone, and a killed guard is replaced after a while.

Creative turns damage off, makes building free, has no raids or guards, and lets `GiveItems` make items from nothing.

## Fire

Fire (D13) burns in Survival and Creative alike, on the host, and every player sees and hears it.

- **Where it starts.** A new world lights a fire at every POI marker of type `fire`: the crash site's
  three, which the first quest asks you to put out. They burn until water puts them out. A part with
  `"flammable": true` (wood, cardboard, carpet, chests, beds...) catches when it sits within `site_radius`
  of such a fire or touches a burning flammable part. Scripts can start fires too (`fire.ignite`).
- **How it burns.** A fire's strength runs from 0 to 1. It grows by `grow` a second; a burning part takes
  up to `part_damage` health a second (less while the fire is weak) until it breaks off, and once it burns
  at `spread_at` it sets every flammable part attached to it alight. Players standing within `player_radius`
  of a fire take up to `player_damage` a second (nothing in Creative). A part under water goes out.
- **Putting it out.** Throw a full water bucket at a fire: the water flies in a lob, and every fire within
  its `splash` of where it lands loses its `douse` strength (vanilla's bucket puts a fire out in one go). The
  bucket comes back empty. A water cannon's jet takes its own `douse` off every fire along it, so a few shots
  put one out; it fires the full buckets in the Water Containers linked to it, ten jets a bucket (the empty
  buckets stay in the container for now). Fill empty buckets at any water, such as the pond by the crash site.
  A **Fire Extinguisher** (crafted at the Fabricator, 3 Metal Block and 1 Chemical make 2, or found in loot crates and
  Bloom Pods) sprays foam while Left click is held: every fire within its `range` and `angle` of where you look loses
  its `douse` strength a second, so a fire goes out in under a second. One sprays for `spray_seconds` and is used up,
  then the next in the stack carries on (`scripts/extinguisher.lua`, numbers in its part's `properties`).
- **Saved** with the world: fires at POI markers by marker (an out one stays out), and a burning part's
  strength on the part.

`game/data/fire.json` holds the numbers (`tick`, `grow`, `ignite`, `spread_at`, `part_damage`,
`player_damage`, `player_radius`, `site_radius`, `site_marker`, `sound`, `max_fires`). It ships with the build,
but its numbers are game rules: a mod changes them with `rules/fire.patch.json`, except `tick` and `max_fires`,
which stay locked (see [Game rules](packaging-guide.md#game-rules)). Each fire plays the `sound` loop (vanilla's
`burn_wood`), its `intensity` following the fire's strength. What puts fires out is Lua
([`fire`](lua-api.md#fire)): `watering.lua` and `water_cannon.lua` read `douse`, `splash` and `range` from
their item's properties.

## Data

- `game/data/survival.json`: health, respawn delay, reach, starting items, fall
  damage, piles, the knockout bag's model (`knockout_bag`, built by `tools/knockout_bag/build_knockout_bag.py`), and
  the empty and full water buckets (`bucket_item`, `water_item`). Unknown keys are refused. A mod changes these
  rules with `rules/survival.patch.json`; only `max_drops` and `aim_tolerance` are locked (see
  [Game rules](packaging-guide.md#game-rules)).
- The Paint Tool (`paint_tool`, a tool: 10 Scrap Metal Blocks, 5 Glass Blocks and 2 Component Kits, 30 s) has to be
  crafted before it paints, and each stroke takes `ammo_per_stroke` of `ammo` (`paint_ammo`, 10 from 2 tomatoes in 3 s)
  from the painter: a click, or a whole drag (`PaintArgs.stroke`, up to `max_parts_per_action` parts and while no gap is
  longer than `stroke_seconds`). Taking paint off is free. These are `paint` in `game/data/building.json`.
- The water bucket has two items: the empty `water_bucket` (the one the Fabricator makes) and the full
  `water_bucket_full`. Filling at water (`CollectWater`) fills every empty bucket the player carries in one go,
  and throwing (`watering.lua`, with `item.replace`) empties one at a time; each swaps one for the other in the
  same slot, so the bucket in hand stays in hand. How it moves in the hand
  is `bucket` in `game/data/character.json`.
- A pool of a liquid with a `bucket` (worldgen `liquids[]`; vanilla's chemical pools name `chemical_bucket`) fills
  buckets with that instead: `CollectWater` there turns every empty bucket carried into a Chemical Bucket (Parts map
  E11). The Fabricator empties one into 5 Chemical and gives the empty Water Bucket back (recipe `chemical`, 5 s: a
  recipe's second output). Its script (`chemical_bucket.lua`) pours `properties.count` Chemical into a container in reach that
  takes it (the Chemical Container), through `inventory.put`, or throws it like water (a green `chemical` projectile
  that waters nothing and puts out no fire); either way the bucket comes back empty. A liquid with only `collect` gives
  its items and leaves the bucket empty.
- Items and parts: `seed`, `soil` and `respawn_point` in part data
  ([Part Data Schema](part-data-schema.md#survival)). Food, weapons, seeds and the water bucket are
  item scripts (`food.lua`, `potato_gun.lua`, `seed.lua`, `bucket.lua` and `watering.lua` in `mods/vanilla/scripts/`)
  with their numbers in `properties`; see [Item scripts](lua-api.md#item-scripts). Soil is a part script,
  `soil.lua` (D2): its `on_update` grows the crop and its `can_activate` / `on_activate` harvest it. A seed's
  `seed` section says what it grows into, how fast, how many seeds a harvest gives back (from `seeds` to `seeds_max`,
  rolled each harvest) and the plant model clients show growing, and the soil's `soil` section how much water it holds
  and its growth speed; the script reads both with `world.item`. The host keeps only what the script stores
  on the soil (seed, growth, water, stage and when growth was last worked out): that is what is saved, sent
  to clients and read by bots and raids. Vanilla's items and soil are authored by the Part Editor batch
  `mods/vanilla/editor/batches/survival.json`; the produce, the seeds, Chemical, Gasoline, the drinks, Fertilizer, Grazer Milk and Grazer Steak by
  `mods/vanilla/editor/batches/consumables.json` (consumables batches C1, C2b and C3). Soil on a Creation (the Growbed, parts map E9) is the same: its state is
  per part, so it follows the part through splits and welds and saves in the Creation. `soil.yield` and `soil.seeds` fix
  its harvest and the seeds it takes (checked by the host in `CheckPlant`, which `farm.can_plant` asks), and `soil.lua`'s
  `charge_item` / `charge_time` properties power it. The Growbed is in `mods/vanilla/editor/batches/parts_farming.json`.
- `game/data/building.json`: `sledge.combat_damage` (players and bots), `sledge.object_damage` (world objects
  without their own) and the knockback (`impulse`, `max_knockback_speed`, `character_mass`, `knockback_lift`,
  `knockback_drag`). A world object's own hit damage is its `sledge_damage` in `worldgen.json`. A mod changes
  `sledge`, `paint`, `breaking` and `lift` with `rules/building.patch.json`; the other sections of `building.json`
  are locked (see [Game rules](packaging-guide.md#game-rules)).

## Action API

| Action | Arguments | Notes |
|--------|-----------|-------|
| `UseItem` | `slot`, `direction?`, `creation?`, `part?`, `hold?` | Runs the item's script: eat, fire, plant, water, spray. With `hold` (an item with `held.hold_use`, the Fire Extinguisher) Use stays held after `on_use`: the script sees `holder.using()` every step until `ReleaseItem`. `direction` (from the caller's eyes) must be within `aim_tolerance` degrees of where they look; omitted means where they look. `creation` and `part` name the part it is used on (the soil a seed goes in, which must be in reach). The script's `can_use` may refuse it; what `on_use` returns is the result (the hitscan potato guns return `{hits}`, the Launcher and Popper `{projectiles}`, food `{health}`, seeds and water the soil). |
| `EquipItem` | `slot?` | Says which scripted item is in hand (its `on_equip` and `on_unequip`); no slot empties the hand. |
| `ActivatePart` | `creation`, `part` | On a soil part in reach with a ripe crop: harvests it (the soil script's `can_activate` and `on_activate`). Refused with "not ripe yet", "Nothing is growing there." or "Make room in your inventory first."; the result is `{items: [{item, count}]}`. (Crops used to use `Harvest`; that action now gathers world objects with `harvest: true`.) |
| `Harvest` | `object` | A world object in reach whose kind has `harvest: true` in `worldgen.json` (an oil geyser): what it gives goes into the inventory. Returns `{taken, left}`; what did not fit is left on the ground. |
| `StartRefine`, `FinishRefine`, `CancelRefine` | `object`, or `creation` and `part`; same; none | Holding E on a resource node (`object`, a tree or rock) or a loose part that refines (`creation`, `part`). `StartRefine` when E goes down, `FinishRefine` once it has been held long enough (refused if the player walked away), `CancelRefine` when it is let go early. `FinishRefine` returns `{taken, left}`. |
| `AimItem` | `on` | Starts (`true`) or stops aiming the item in hand: right click held with a weapon. |
| `ReleaseItem` | `slot?` | Lets go of Use after a held use (`UseItem` with `hold`): the script's `on_use_release`. Letting go when nothing is held does nothing. |
| `ReloadItem` | `slot` | Reloads the item in the slot (its script's `on_reload_item`). |
| `SetFriendlyFire` | `on` | Host only: turns friendly fire on or off. |
| `CollectWater` | `point`, `slot?` | A point on water in reach; turns every empty `bucket_item` the caller carries into a full `water_item` (the one in `slot`, or the first, first), each in the same slot when it was the last of its stack; with no room for the rest, the others stay empty. In a pool of a liquid with a `bucket` item they turn into that instead (a Chemical Bucket), and in one with only `collect` the caller gets those items and the buckets stay empty. Returns `{slot}`, where the full bucket is. |
| `SetSpawn` | `creation`, `part` | A `respawn_point` part in reach. |
| `DropItems` | `slot`, `count`, `at`, `from` | Tosses the stack (or `count` of it) in front of you; `from` takes it from a container in reach instead of your inventory, and `at` places it there with Force build (it then stays). |
| `PickUp` | `drop` | A pile within `pickup_reach`. |
| `Respawn` | none | A dead player after the delay; a living one goes back to their spawn. |
| `GetFires` | none | Every fire: `[{id, position, strength, creation?, part?, poi?, site?, marker?}]`. Fires at POI markers are listed out ones too (strength 0). |
| `CollectItems` | `creation`, `part`, `kind`, `drop?`, `target_creation?`, `target_part?` | Part scripts only (a Vacuum Pump's or Resource Collector's `part.collect`): takes a pile (`drop`), a loose piece (`target_creation`), a ripe crop (`target_creation`, `target_part`) or water (`kind` `water`) within the part's `intake` into its own inventory or the containers its ports send to. Returns `{items}`. |
| `EjectItems` | `creation`, `part`, `item?`, `count` | Part scripts only (`part.eject`): throws items from the part's inventory or the containers its ports take from out of its intake as a pile. Returns `{item, count, drop}`. |
| `GetNetwork`, `GetPickups` | `creation`, `part` | Queries: what a part's IO ports reach through pipes, and what its intake reaches. |
| `GetVitals`, `GetSoil` | none; `creation`, `part` | Queries. `GetSoil` shows what the soil's script last stored; a parked farm's growth is worked out when it next runs (or is harvested). |

A dead player's other actions are refused. Item scripts hurt and heal through the same host-side
primitives bots use (`SurvivalActions.Hit`, `Hitscan`, `Splash`, `Launch` for projectiles, `Heal`,
`Damage`), so the friendly fire and Creative rules apply to them the same way. Projectiles fly on the host;
every client is sent each launch and draws it along its arc until the host says it landed. The Potato
Launcher and Popper lob theirs (D14): `speed` in their properties sets the throw, and they burst
(`radius`) where they land; the Popper also bursts when its `fuse` runs out.
