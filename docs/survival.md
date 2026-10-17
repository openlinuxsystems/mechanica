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

## Playing

| Key | What it does |
|-----|--------------|
| 6 | Take the item in hand (the sixth tool). Q and E choose among the usable items you carry. |
| Left click (item in hand) | Use it (all Lua item scripts): eat food, fire a weapon, plant a seed in the soil you aim at or water it. With a bucket or nothing usable in hand, aiming at the sea fills a bucket. |
| G (item in hand) | Drop one of the item on the ground. |
| E | Harvest the ripe crop you aim at, sleep in the bed you aim at (you respawn there), or pick up the nearest pile of items. |
| E (dead) | Respawn, once the respawn delay is over. |

- **Health** goes down from weapons, the Sledgehammer, splash, and falls faster than
  `fall.min_speed`. It comes back by eating. At 0 you die: everything you carry falls in a pile where
  you stood, and you respawn at your bed, or at the world spawn if you have none (or it was broken).
- **Farming:** place soil, plant a seed, and water it. A crop grows only while its soil holds water,
  using a second of water per second of growth, through four stages. Harvest it ripe (E) for its crop
  and usually a seed back. Crops keep growing while nobody is near (the soil catches up from the world
  clock when it next runs) and are saved with the soil's Creation.
- **Building costs items.** Placing a part takes one from your inventory; removing it gives it back
  (or drops it when your inventory is full). Craft parts at a Mini Fabricator or Fabricator.
- **Breaking things:** a part breaks off at 0 health. A container that breaks spills what it held.
  Trees, rocks and bushes with `health` break into resources (`drops` in `world/worldgen.json`).
  A Body that stops suddenly (a crash) damages the parts on the side that hit, more for weaker parts.
- **Piles** of items on the ground last `drop_lifetime` seconds.

Creative turns damage off, makes building free and lets `GiveItems` make items from nothing.

## Data

- `game/data/survival.json`: health, respawn delay, PvP, reach, starting items, fall and impact
  damage, piles. Unknown keys are refused.
- Items and parts: `seed`, `soil` and `respawn_point` in part data
  ([Part Data Schema](part-data-schema.md#survival)). Food, weapons, seeds and the water bucket are
  item scripts (`food.lua`, `potato_gun.lua`, `seed.lua` and `watering.lua` in `mods/vanilla/scripts/`)
  with their numbers in `properties`; see [Item scripts](lua-api.md#item-scripts). Soil is a part script,
  `soil.lua` (D2): its `on_update` grows the crop and its `can_activate` / `on_activate` harvest it. A seed's
  `seed` section says what it grows into and how fast, and the soil's `soil` section how much water it holds
  and its growth speed; the script reads both with `world.item`. The host keeps only what the script stores
  on the soil (seed, growth, water, stage and when growth was last worked out): that is what is saved, sent
  to clients and read by bots and raids. Vanilla's items and soil are authored by the Part Editor batch
  `mods/vanilla/editor/batches/survival.json`.
- `game/data/building.json`: `sledge.player_damage` and `sledge.object_damage`.

## Action API

| Action | Arguments | Notes |
|--------|-----------|-------|
| `UseItem` | `slot`, `direction?`, `creation?`, `part?` | Runs the item's script: eat, fire, plant, water. `direction` (from the caller's eyes) must be within `aim_tolerance` degrees of where they look; omitted means where they look. `creation` and `part` name the part it is used on (the soil a seed goes in, which must be in reach). The script's `can_use` may refuse it; what `on_use` returns is the result (the hitscan potato guns return `{hits}`, the Launcher and Popper `{projectiles}`, food `{health}`, seeds and water the soil). |
| `EquipItem` | `slot?` | Says which scripted item is in hand (its `on_equip` and `on_unequip`); no slot empties the hand. |
| `ActivatePart` | `creation`, `part` | On a soil part in reach with a ripe crop: harvests it (the soil script's `can_activate` and `on_activate`). Refused with "not ripe yet", "Nothing is growing there." or "Make room in your inventory first."; the result is `{items: [{item, count}]}`. (It replaced the `Harvest` action.) |
| `CollectWater` | `point` | A point on the sea in reach; adds one `water_item`. |
| `SetSpawn` | `creation`, `part` | A `respawn_point` part in reach. |
| `DropItems` | `slot`, `count` | `count` omitted drops the stack. |
| `PickUp` | `drop` | A pile within `pickup_reach`. |
| `Respawn` | none | A dead player after the delay; a living one goes back to their spawn. |
| `GetVitals`, `GetSoil` | none; `creation`, `part` | Queries. `GetSoil` shows what the soil's script last stored; a parked farm's growth is worked out when it next runs (or is harvested). |

A dead player's other actions are refused. Item scripts hurt and heal through the same host-side
primitives bots use (`SurvivalActions.Hit`, `Hitscan`, `Splash`, `Launch` for projectiles, `Heal`,
`Damage`), so the PvP and Creative rules apply to them the same way. Projectiles fly on the host;
every client is sent each launch and draws it along its arc until the host says it landed. The Potato
Launcher and Popper lob theirs (D14): `speed` in their properties sets the throw, and they burst
(`radius`) where they land; the Popper also bursts when its `fuse` runs out.
