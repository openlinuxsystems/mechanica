# Playing Mechanica as an AI

Mechanica is a physics sandbox: you build machines out of parts on a 0.25 m grid, survive on an alien
world, farm, fight bots and follow a story. Units are SI everywhere (metres, kilograms, seconds, newtons).
The host simulates everything at 60 physics ticks per second and checks every request, so you can
only do what a human player could do with the same scope.

Every method below is in [reference.md](reference.md) with its exact params. Call
`mechanica.describe` when in doubt; never guess an id or a param name.

## 1. Connect and join

1. `mechanica.auth {"token": "..."}` if the host set a token. The answer tells you nothing more; call
   `mechanica.info` to see your `scopes`, the current `tick` and every method name.
2. `mechanica.join {"name": "YourName"}` gives you a player body of your own. Others see you as
   "YourName (AI)". From now on every action acts as that player: your inventory, your health, your
   quests, your reach. Use the same name every session to get your saved inventory and position back.
3. `mechanica.subscribe {"topics": ["hurt", "vitals", "dialogue", "quest", "chat", "messages"]}` so the
   game tells you when things happen to you instead of you asking over and over. Add `raid`, `bots`,
   `drop`, `fire` and `actions` (what other players do) when they matter.

Without joining you act as an "external" agent on the host. That is fine for looking around and for
admin work, but many actions (`GetHotbar`, `Craft`, using items) need a player.

## 2. Look before you act

| Question | Method |
|----------|--------|
| Where am I, which way do I face, how healthy am I? | `GetPlayer` (`position`, `eye`, `yaw`, `look`, `health`, `held`) |
| What is the world like? | `GetWorld` (seed, mode, difficulty, time, sea level, spawn, players) |
| What is in front of me? | `Raycast` (defaults to your eyes and view; returns what it hits and the distance) |
| What is the ground like around me? | `SampleTerrain` (height, biome, slope, water depth on a grid) |
| Trees, rocks, items on the ground? | `GetObjects`, `GetDrops` |
| Places the story has revealed? | `GetPois` |
| Machines in the world? | `ListCreations`, then `GetCreation` for parts, bodies and joints |
| What can I build or make? | `ListParts`, `ListItems`, `GetPart`, `ListRecipes` |
| What do I carry? | `GetInventory {"inventory": {"player": <your peer>}}` (a chest or station is `{"creation": c, "part": p}`), `GetHotbar` |
| What does the story want? | `GetQuests`, `GetQuestBook` |

Queries never change anything, cost nothing in game terms, and work even while you are dead.

## 3. Move

`SetPlayerInput` holds keys for a number of ticks (60 per second):

```json
{"move_z": 1, "yaw": 1.57, "sprint": true, "ticks": 120}
```

- `yaw` is absolute, in radians: 0 looks down −z, π/2 looks down −x (turning left is positive). To
  face a point from your position, `yaw = atan2(-(tx - x), -(tz - z))`.
- `move_z` 1 walks forward along where you look, `move_x` 1 strafes right. `jump`, `sprint` and `crawl`
  are held for the same ticks. Leaving out `yaw` or `pitch` keeps your current view.
- After the ticks run out you stand still. A new call replaces the old one, so steer in short pieces
  (30 to 120 ticks) and check `GetPlayer` between them. If your position stops changing while you are
  holding forward, you are blocked: jump, turn, or go around.
- Reach is limited (a few metres) for building, picking up, using stations and quest markers. Walk close
  first; a refusal saying something is out of reach means move nearer.

## 4. Build

- A new machine: `PlacePart` with `part`, `cell` `[0, 0, 0]` and a world `position`. Then attach more
  parts to it with `creation` and cells in its own grid. `PlaceParts` does many at once.
- Rotations are strings like `"+x+y"`: where the part's own +x and +y point.
- `GetPart` shows a part's size, sockets, joints and `settings` (what `ConfigurePart` accepts).
- Bearings, pistons and suspension are the only moving joints; everything else welded together is one
  rigid body. Drive bearings and pistons from seats and controllers like a player, through
  `EnterSeat`, `ActivatePart` and the part's menu (`CommandPart`). Motion primitives such as
  `ApplyForce` and `SetBearingTarget` belong to part scripts and are refused to players.
- In Survival every part placed costs one from your inventory. Craft parts at a Fabricator, or a short
  list of them at a Portable Fabricator, with `Craft` (recipes from `ListRecipes`).

## 5. Survive

- Health is the only meter. Eat with `UseItem` on food. Watch `hurt` and `vitals` events.
- Gather: break trees and rocks with the Sledgehammer (`Sledge` on an object id from `GetObjects`),
  refine resource nodes with `StartRefine` then `FinishRefine`, and `PickUp` piles from `GetDrops`.
- Oil and chemicals: `Harvest` an oil geyser (`GetObjects` with type `oil_geyser`, in desert and burned
  forest oil ponds) for Crude Oil; it wells up again an hour later. `CollectWater` with an empty bucket
  aimed into a chemical pool gives a Chemical. Standing in a chemical pool hurts.
- Farm: place soil, plant a seed with `UseItem` on the soil part, keep it watered (`CollectWater` at
  water, then use the bucket), and harvest ripe crops with `ActivatePart` on the soil.
- Sleep in a bed with `SetSpawn` so you respawn there. When you die, everything you carried drops where
  you died; `Respawn` once the delay is over and go back for it.
- Fire hurts. Put it out by using a water bucket aimed at it.
- Bots attack players they see, and raids send waves of them at your base (`raid` and `bots` events). Hide in a shelter or fight with weapons via `UseItem`.

## 6. The story

`GetQuests` says the current step of each quest. Steps send you to revealed POIs (`GetPois` gives their
markers), ask you to talk to characters (`QuestInteract` near a marker, answers with
`ChooseQuestOption`), or to make or bring things. The `dialogue` and `quest` events tell you when
something changes.

## 7. Being a good player

- Other players are people. Say what you are doing in chat (`SendChat`) when you share a world, and do
  not take apart their machines unless asked.
- If a request is refused, read `error.message`: it says why (out of reach, not enough items, wrong
  mode). Fix the cause instead of repeating the same call. Errors are listed at the end of
  [reference.md](reference.md#error-codes).
- Keep notes as you go. When something behaves in a way a player would not expect, follow
  [bug-hunting.md](bug-hunting.md).
