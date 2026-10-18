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
as a Python module), which also writes the animation set and the hand-held tools; their procedural textures
are baked by `tools/characters/character_textures.py`.

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
| `bucket_fill` | Water pouring into a water bucket being filled. | `bucket_fill` |

### First-person arms

In first person you see your character's own forearms and hands, holding the tool or item and playing its swings.
They are cut from the character's mesh when it loads (the triangles skinned to the forearm, hand, finger and thumb
bones), so a new character or skin gets them with no extra work. Where they sit in view is `first_person_offset`
and `first_person_pitch_degrees` in `game/data/character.json`. A sleeve that crosses the elbow is cut open there;
vanilla's build closes each opening with a black cap that shares the cut edge's vertices and weights (hidden inside the
sleeve on the full body), so first person shows no hollow tube. A skin can do the same.

A clip can have a first-person version, named `<clip>_fp` (`hold_heavy_fp`, `hold_heavy_block_fp`, the
three `sledge_swing…_fp`, the Refine Tool's three and the Connect Tool's six). The arms play it instead of the shared clip when it exists: the
Sledgehammer is held low and swung closer to the camera there, so the head comes down through the middle of the view,
and the Refine Tool's blade points at the crosshair. Everything else plays the same clip in both views.

### Seeing it in game

`tools/capture-characters.sh OUT_DIR [character ...]` starts a world and renders each character to PNGs: standing,
holding and swinging tools, looking up and down, crouching, moving, and in first person with each tool. It needs a
display, or `xvfb-run` without one.

## Animations

The animation set is shared by every character: `idle`, `walk`, `jog`, `run`, `crouch_idle`, `crouch_walk`,
`jump`, `fall`, `land`, `swim_idle`, `swim` and `sit` move the whole body; `hold_tool`, `hold_heavy`,
`hold_heavy_block` (the Sledgehammer raised across the chest while right click blocks, in Creative), `hold_gun`, `hold_item`,
`hold_refine` (the Refine Tool scraping at a resource node, a stroke every 0.75 s), `hold_connect` (the Connect Tool at
chest height, tip forward, with a slow sway), `hold_connect_aim` (raised and aimed while a node is under the crosshair or a
drag is on), `hold_weld` and `hold_weld_aim` (the Weld Tool held, and raised while a part is selected) and the one-shot
gestures (`sledge_swing`, `tool_use`, `weld` (the Weld Tool pushed in and held while a weld registers),
`paint_spray`, `fire_gun`, `eat`, `throw`, `plant`, `pour`, `interact`, `hurt`, `connect_click` when a link is made or
taken away, `connect_reverse` when a bearing is reversed) move only the upper body
from the spine up, over whatever the legs are doing. Every peer works out each player's pose from what it
already knows (speed, on the ground, swimming, crouching, seated, what is in hand), so animation costs no
network traffic beyond a byte for the tool in hand and one for each swing. The Sledgehammer has three swings (`sledge_swing` overhead,
`sledge_swing_side` from the right and `sledge_swing_back` from the left); which one a click plays is picked at
random from that swing count, never the same twice running, so every peer plays the same one. The hit lands at the click, so
each swing starts past its wind-up (`gesture_start` in `game/data/character.json`) with the head already coming down. The Refine Tool, shown
only while Use is held on a resource node, comes with a draw from the hip (`refine_draw`) and a stow back to it
(`refine_stow`); every peer plays them when it sees that player's hand take or drop the tool, chips fly off the node at
each stroke (sparks off metal) and the `refiner_*` sounds play, all from the same prop byte. The Connect Tool draws from
the hip (`connect_draw`, 0.5 s) and stows back to it (`connect_stow`, 0.45 s); its tip and display glow cyan, pulse while
it is raised and flash for 0.2 s on each `connect_click` (a dip then a flash on `connect_reverse`). The Weld Tool
draws from the hip (`weld_draw`) and stows back to it (`weld_stow`); during `weld` its clamping arms
(`weld_tool_arm_l`, `weld_tool_arm_r`) close round its tip, the tip (`weld_tool_tip`) spins and its blue ring
(`weld_tool_glow`) brightens (`WeldMotion`), and blue sparks ride on the host's `weld_complete` sound.
`tools/characters/render_weld_tool.py OUT_DIR` renders its prop like its reference sheet, arms open and closed.

Timing lives in `game/data/character.json` (engine settings, not moddable): the speed each moving clip
is authored at, where in a cycle the feet land, crossfade times, how far the body turns before the feet
follow, and which items are held how. A mod item gets the pose of its category (`consumable` eats,
`weapon` throws) unless it is listed in `item_poses`.
