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
| 6 | Take the item in hand (the sixth tool). Q and E choose among the usable items you carry. |
| Left click (item in hand) | Use it (all Lua item scripts): eat food, fire a weapon, plant a seed in the soil you aim at or water it, or throw the water out of a full bucket. With an empty water bucket in hand, aiming at water fills it. |
| G (item in hand) | Drop one of the item on the ground. |
| E | Harvest the ripe crop you aim at, sleep in the bed you aim at (you respawn there), or pick up the nearest pile of items. |
| E (dead) | Respawn, once the respawn delay is over. |

- **Health** goes down from weapons, the Sledgehammer, splash, and falls faster than
  `fall.min_speed`. It comes back by eating. At 0 you die: everything you carry falls where
  you stood in the knockout bag, a yellow canvas drawstring bag (tools you crafted, like the Connect Tool, stay with you), and you respawn at your bed, or at the world spawn if you have none (or it was broken).
- **Farming:** aim a Soil Bag at the ground (no steeper than 17°) to spill out a 3×3 soil mound (with Force build held,
  or aimed anywhere but the terrain, the bag itself is set down; picking a mound up gives the bag back). Plant a seed
  and water it. Soil looks dry, lightly watered, fully wet or drying, and empty soil dries out in about a game day. A crop grows only while its soil holds water,
  using a second of water per second of growth, through four stages. Harvest it ripe (E) for its crop
  and usually a seed back. Crops keep growing while nobody is near (the soil catches up from the world
  clock when it next runs) and are saved with the soil's Creation.
- **Building costs items.** Placing a part takes one from your inventory; removing it gives it back
  (or drops it when your inventory is full). Craft parts at a Mini Fabricator or Fabricator.
- **Breaking things:** a part breaks off at 0 health. A container that breaks spills what it held.
  Trees, rocks and bushes with `health` break into resources (`drops` in `world/worldgen.json`).
  A Body that stops suddenly (a crash) damages the parts on the side that hit, more for weaker parts.
- **Piles** of items on the ground last `drop_lifetime` seconds.
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
  put one out. Fill empty buckets at any water, such as the pond by the crash site.
- **Saved** with the world: fires at POI markers by marker (an out one stays out), and a burning part's
  strength on the part.

`game/data/fire.json` holds the numbers (`tick`, `grow`, `ignite`, `spread_at`, `part_damage`,
`player_damage`, `player_radius`, `site_radius`, `site_marker`, `sound`, `max_fires`); like the other files
in `game/data` it ships with the build and is not moddable. Each fire plays the `sound` loop (vanilla's
`burn_wood`), its `intensity` following the fire's strength. What puts fires out is Lua
([`fire`](lua-api.md#fire)): `watering.lua` and `water_cannon.lua` read `douse`, `splash` and `range` from
their item's properties.

## Data

- `game/data/survival.json`: health, respawn delay, reach, starting items, fall and impact
  damage, piles, the knockout bag's model (`knockout_bag`, built by `tools/knockout_bag/build_knockout_bag.py`), and
  the empty and full water buckets (`bucket_item`, `water_item`). Unknown keys are refused.
- The water bucket has two items: the empty `water_bucket` (the one the Fabricator makes) and the full
  `water_bucket_full`. Filling one at water (`CollectWater`) or throwing its water (`watering.lua`, with
  `item.replace`) swaps one for the other in the same slot, so it stays in hand. How it moves in the hand
  is `bucket` in `game/data/character.json`.
- Items and parts: `seed`, `soil` and `respawn_point` in part data
  ([Part Data Schema](part-data-schema.md#survival)). Food, weapons, seeds and the water bucket are
  item scripts (`food.lua`, `potato_gun.lua`, `seed.lua`, `bucket.lua` and `watering.lua` in `mods/vanilla/scripts/`)
  with their numbers in `properties`; see [Item scripts](lua-api.md#item-scripts). Soil is a part script,
  `soil.lua` (D2): its `on_update` grows the crop and its `can_activate` / `on_activate` harvest it. A seed's
  `seed` section says what it grows into and how fast, and the soil's `soil` section how much water it holds
  and its growth speed; the script reads both with `world.item`. The host keeps only what the script stores
  on the soil (seed, growth, water, stage and when growth was last worked out): that is what is saved, sent
  to clients and read by bots and raids. Vanilla's items and soil are authored by the Part Editor batch
  `mods/vanilla/editor/batches/survival.json`.
- `game/data/building.json`: `sledge.combat_damage` (players and bots), `sledge.object_damage` (world objects
  without their own) and the knockback (`impulse`, `max_knockback_speed`, `character_mass`, `knockback_lift`,
  `knockback_drag`). A world object's own hit damage is its `sledge_damage` in `worldgen.json`.

## Action API

| Action | Arguments | Notes |
|--------|-----------|-------|
| `UseItem` | `slot`, `direction?`, `creation?`, `part?` | Runs the item's script: eat, fire, plant, water. `direction` (from the caller's eyes) must be within `aim_tolerance` degrees of where they look; omitted means where they look. `creation` and `part` name the part it is used on (the soil a seed goes in, which must be in reach). The script's `can_use` may refuse it; what `on_use` returns is the result (the hitscan potato guns return `{hits}`, the Launcher and Popper `{projectiles}`, food `{health}`, seeds and water the soil). |
| `EquipItem` | `slot?` | Says which scripted item is in hand (its `on_equip` and `on_unequip`); no slot empties the hand. |
| `ActivatePart` | `creation`, `part` | On a soil part in reach with a ripe crop: harvests it (the soil script's `can_activate` and `on_activate`). Refused with "not ripe yet", "Nothing is growing there." or "Make room in your inventory first."; the result is `{items: [{item, count}]}`. (It replaced the `Harvest` action.) |
| `CollectWater` | `point`, `slot?` | A point on water in reach; turns one empty `bucket_item` (the one in `slot`, or the first) into a full `water_item`, in the same slot when it was the last. Returns `{slot}`, where the full bucket is. |
| `SetSpawn` | `creation`, `part` | A `respawn_point` part in reach. |
| `DropItems` | `slot`, `count` | `count` omitted drops the stack. |
| `PickUp` | `drop` | A pile within `pickup_reach`. |
| `Respawn` | none | A dead player after the delay; a living one goes back to their spawn. |
| `GetFires` | none | Every fire: `[{id, position, strength, creation?, part?, poi?, site?, marker?}]`. Fires at POI markers are listed out ones too (strength 0). |
| `GetVitals`, `GetSoil` | none; `creation`, `part` | Queries. `GetSoil` shows what the soil's script last stored; a parked farm's growth is worked out when it next runs (or is harvested). |

A dead player's other actions are refused. Item scripts hurt and heal through the same host-side
primitives bots use (`SurvivalActions.Hit`, `Hitscan`, `Splash`, `Launch` for projectiles, `Heal`,
`Damage`), so the friendly fire and Creative rules apply to them the same way. Projectiles fly on the host;
every client is sent each launch and draws it along its arc until the host says it landed. The Potato
Launcher and Popper lob theirs (D14): `speed` in their properties sets the throw, and they burst
(`radius`) where they land; the Popper also bursts when its `fuse` runs out.
