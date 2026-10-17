# Survival

A Survival world (the default; `--creative` or the main menu's "Play creative" starts Creative) has
one meter, health (D12: no stamina, no hunger). The host decides everything through the Action API,
so it works the same for every player in a multiplayer game.

## Playing

| Key | What it does |
|-----|--------------|
| 6 | Take the item in hand (the sixth tool). Q and E choose among the usable items you carry. |
| Left click (item in hand) | Eat food, fire a weapon, plant a seed in the soil you aim at, water it, or fill a bucket at the sea. |
| G (item in hand) | Drop one of the item on the ground. |
| F | Harvest the ripe crop you aim at, sleep in the bed you aim at (you respawn there), or pick up the nearest pile of items. |
| R (dead) | Respawn, once the respawn delay is over. |

- **Health** goes down from weapons, the Sledgehammer, splash, and falls faster than
  `fall.min_speed`. It comes back by eating. At 0 you die: everything you carry falls in a pile where
  you stood, and you respawn at your bed, or at the world spawn if you have none (or it was broken).
- **Farming:** place soil, plant a seed, and water it. A crop grows only while its soil holds water,
  using a second of water per second of growth, through four stages. Harvest it ripe for its crop
  and usually a seed back. Crops are saved with the soil's Creation.
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
- Items and parts: `food`, `seed`, `watering`, `weapon`, `soil` and `respawn_point` in part data
  ([Part Data Schema](part-data-schema.md#survival)). Vanilla's are authored by the Part Editor batch
  `mods/vanilla/editor/batches/survival.json`.
- `game/data/building.json`: `sledge.player_damage` and `sledge.object_damage`.

## Action API

| Action | Arguments | Notes |
|--------|-----------|-------|
| `Eat` | `slot` | Refused at full health. |
| `Fire` | `slot`, `direction` | From the caller's eyes; must be within `aim_tolerance` degrees of where they look. Returns what was hit. |
| `Plant`, `Water`, `Harvest` | `creation`, `part`, `slot` | On a soil part in reach. |
| `CollectWater` | `point` | A point on the sea in reach; adds one `water_item`. |
| `SetSpawn` | `creation`, `part` | A `respawn_point` part in reach. |
| `DropItems` | `slot`, `count` | `count` omitted drops the stack. |
| `PickUp` | `drop` | A pile within `pickup_reach`. |
| `Respawn` | none | A dead player after the delay; a living one goes back to their spawn. |
| `GetVitals`, `GetSoil` | none; `creation`, `part` | Queries. |

A dead player's other actions are refused. Weapons are generic and data-driven for now; Lua item
behaviour can take them over once the MoonSharp host exists.
