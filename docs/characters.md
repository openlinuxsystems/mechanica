# Player Characters

Every player plays as a character: vanilla has the **Male Scrapper** and the **Female Scrapper**. You pick
one under **Character** on the New game screen, and anyone joining a world for the first time is asked
before they play. The pick is kept for that player in that world (it is saved with the world) and every
other player sees it. Characters only change how you look and sound: they are the same height, share one
skeleton, animation set and collision capsule, and move and play exactly the same.

## Adding a character (a skin)

A character is a JSON file in a mod's `characters/` folder plus a skinned glTF mesh:

```json
// mods/my_mod/characters/robot.json
{
  "id": "robot",
  "name": "Robot",
  "description": "A tin scrapper.",
  "mesh": "meshes/characters/robot.glb",
  "order": 10,
  "sounds": { "footstep": "robot_clank", "jump": "robot_hop" }
}
```

| Field | Meaning |
|-------|---------|
| `id` | Unique in the mod. Saves store it as `my_mod:robot`. |
| `name` | Shown on the picker. |
| `description` | Optional; shown under the name. |
| `mesh` | A `.glb` or `.gltf` in the mod (`other_mod:path` for another mod's). |
| `order` | Optional; lower comes first on the picker. Vanilla uses 0 and 1. The first is the default for a player who has not chosen yet. |
| `sounds` | Optional; local sound name to sound event id (see below). |

### The mesh

The mesh must be skinned to the shared humanoid skeleton. It needs every bone of
`vanilla:meshes/characters/humanoid_animations.glb`, by name:

```
root, hips, spine, chest, neck, head,
shoulder_l, upper_arm_l, forearm_l, hand_l, fingers_l, thumb_l   (and the same with _r)
thigh_l, shin_l, foot_l, toe_l                                  (and the same with _r)
```

Extra bones (hair, a tail) are fine; no clip moves them. Keep the bone rest poses of the vanilla
skeleton, since the clips are authored against them: start from `scrapper_male.glb`, change the mesh,
and keep the armature. Model in metres, standing on the origin, facing +Z (Blender's -Y, the default glTF
export), about 1.8 m tall. Vanilla's models are made by `tools/characters/build_characters.py` (Blender
as a Python module), which also writes the animation set and the hand-held tools.

The loader refuses a character whose mesh is missing, is not skinned, or lacks a rig bone (MD1201), and
warns when two files share an id (MD1202).

### Sounds

A character binds these local names; a bare id means the character's own mod's event when it has one,
otherwise vanilla's.

| Name | When | Default |
|------|------|---------|
| `footstep` | A foot lands on the ground. | `footstep_default` |
| `footstep_metal` | A foot lands on a metal or scrap part. | `footstep_metal` |
| `footstep_wood` | A foot lands on a wooden part. | `footstep_wood` |
| `footstep_water` | A foot lands while wading. | `footstep_water` |
| `jump` | The player jumps. | `player_jump` |
| `land` | The player lands. | `player_land` |
| `land_hard` | The player lands from a long fall. | `player_land_hard` |
| `swim_stroke` | Each stroke while swimming. | `swim_stroke` |
| `water_enter` / `water_exit` | Starting and stopping swimming. | `water_enter` / `water_exit` |
| `tool_equip` | Taking a different tool or item in hand. | `tool_equip` |

## Animations

The animation set is shared by every character: `idle`, `walk`, `jog`, `run`, `crouch_idle`, `crouch_walk`,
`jump`, `fall`, `land`, `swim_idle`, `swim` and `sit` move the whole body; `hold_tool`, `hold_heavy`,
`hold_gun`, `hold_item` and the one-shot gestures (`hammer_swing`, `sledge_swing`, `tool_use`, `weld`,
`paint_spray`, `fire_gun`, `eat`, `throw`, `plant`, `pour`, `interact`, `hurt`) move only the upper body
from the spine up, over whatever the legs are doing. Every peer works out each player's pose from what it
already knows (speed, on the ground, swimming, crouching, seated, what is in hand), so animation costs no
network traffic beyond a byte for the tool in hand and one for each swing.

Timing lives in `game/data/character.json` (engine settings, not moddable): the speed each moving clip
is authored at, where in a cycle the feet land, crossfade times, how far the body turns before the feet
follow, and which items are held how. A mod item gets the pose of its category (`consumable` eats,
`weapon` throws) unless it is listed in `item_poses`.
