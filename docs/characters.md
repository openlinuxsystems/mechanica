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
| `order` | Optional; lower comes first on the picker. Vanilla uses 0 and 1, and wins a tie. The first is the default for a player who has not chosen yet. |
| `sounds` | Optional; local sound name to sound event id (see below). |
| `npc` | Optional; `true` makes a non-player character: it never shows on the picker and no player can play it. It stands at POI `npc` markers whose `ref` is its id (see below). |

### Non-player characters

A character with `"npc": true` is drawn standing idle at every POI `npc` marker that names it, on every
peer, facing the marker's `yaw`, with a player-sized capsule that players, bots and Creations bump into.
A bare `ref` names a character of the POI's own mod; `other_mod:id` names another mod's. Quests talk to
it through the marker (an `interact` objective with `"marker": "npc"`, see [Quests](quests.md)). Vanilla's
farmer at the crash site is one:

```json
// mods/vanilla/characters/farmer.json
{ "id": "farmer", "name": "Farmer", "mesh": "meshes/characters/farmer.glb", "npc": true }
```

An `npc` marker whose `ref` names no character still works for quests; it just has no figure.

### The mesh

The mesh must be skinned to the shared humanoid skeleton. It needs every bone of
`vanilla:meshes/characters/humanoid_animations.glb`, by name:

```
root, hips, spine, chest, neck, head,
shoulder_l, upper_arm_l, forearm_l, hand_l, fingers_l, thumb_l   (and the same with _r)
thigh_l, shin_l, foot_l, toe_l                                  (and the same with _r)
```

The hands should also have the finger bones, three down each finger under `fingers_l` and two more
down the thumb under `thumb_l` (and the same with `_r`):

```
index_1_l, index_2_l, index_3_l, middle_1_l .. middle_3_l, ring_1_l .. ring_3_l, pinky_1_l .. pinky_3_l,
thumb_2_l, thumb_3_l
```

They are optional. A hand without them bends all four fingers at the knuckles on `fingers_l`, following the
clips' middle finger, so an older one-bone hand still closes round what it holds, only less well.

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

## Changing another mod's character

Override, patch or remove it through `overrides/<mod>/characters/`; see the [Packaging Guide](packaging-guide.md#overriding-bots-quests-pois-and-characters).

## Animations

A seated player fits its seat (`seat.cushion`, `seat.hands` and `seat.pose` in [the part data](part-data-schema.md#logic-settings-and-seats)):
the legs blend `sit` and `sit_low` so the hips land on the cushion, whatever its height (on a saddle, `sit_saddle` or
`sit_saddle_ride` astride it, the body lifted or dropped onto the cushion), and the body faces the seat's front
while the head looks around. While it sits, the body rides along: sitting down sinks it into the seat and lets it settle, then
each change in the seat's own motion pushes springs that bob the hips and sway the upper body (a bump throws it up,
braking tips it forward, a turn leans it out), scaled by the seat's `seat.bounce`, so a smooth road barely shows and rough
ground throws the driver about. The springs are `seat` in `game/data/character.json`. A driver's hands stay on the controls as they move: the
seat's model turns its wheel or bars and presses its pedals by the seat script's `wheel`, `gas` and `brake` readouts (its
`display.clips.scrub`, eased at their `rate`), and the body holds `sit_steer` (`sit_low_steer` on a low seat,
`sit_saddle_steer` on a saddle) at the same point, 0 s full left to the clip's end full right, so the hands ride round
with the rim and the head looks into the turn; the feet press the pedals with `sit_gas` and `sit_brake` (`sit_low_gas`,
`sit_low_brake`) blended in. A character without those clips holds the wheel still.

The animation set is shared by every character: `idle`, `idle_look` and `idle_stretch` (fidgets), `turn_l` and `turn_r`
(stepping round on the spot), `walk`, `walk_back` (stepping backward: onto the ball of the foot, weight back, arms
out for balance; played in reverse on the walk's beat, so it has the walk's length; without it the walk plays in
reverse), `jog`, `run`, `strafe_l` and `strafe_r` with `strafe_jog_l`, `strafe_jog_r`, `strafe_run_l` and `strafe_run_r`
(the walk, jog and run stepping on a diagonal 45 degrees to the left or right of ahead, the feet still pointing ahead;
each has the length of the clip it stands in for. Moving sideways they take up to all of it and the legs turn the
rest, so the feet always follow the way the body goes; without all six the legs turn up to `max_body_turn_degrees`
instead), `crouch_idle`, `crouch_walk`,
`jump`, `jump_run` (a running leap), `fall`, `land`, `land_hard` (a long fall, a hand to the ground), `swim_idle`, `swim`, `climb` (a ladder: hands and feet on rungs 0.25 m apart, played forward or back as the climber moves up or down), `sit` (a chair, hands on a wheel), `sit_low` (a low seat, legs out in
front), `sit_ride` and `sit_ride_low` (the same with the hands resting on the thighs, for passengers), `sit_saddle` (astride a
saddle, leaning forward with the hands on handlebars), `sit_saddle_ride` (astride, sitting up with the hands on the thighs)
and the pedal versions `sit_gas`, `sit_brake`, `sit_low_gas` and `sit_low_brake` move the whole body; `hold_tool`, `hold_heavy`,
`hold_heavy_block` (the Sledgehammer raised across the chest while right click blocks, in Creative), `hold_gun`, `hold_item`,
`hold_refine` (the Refine Tool scraping at a resource node, a stroke every 0.75 s), `hold_connect` (the Connect Tool at
chest height, tip forward, with a slow sway), `hold_connect_aim` (raised and aimed while a node is under the crosshair or a
drag is on), `hold_weld` and `hold_weld_aim` (the Weld Tool held, and raised while a part is selected) and the one-shot
gestures (`sledge_swing`, `tool_use`, `weld` (the Weld Tool pushed in and held while a weld registers),
`paint_spray`, `fire_gun`, `eat`, `drink`, `throw`, `plant`, `pour`, `interact`, `hurt`, `connect_click` when a link is made or
taken away, `connect_reverse` when a bearing is reversed) move only the upper body
from the spine up, over whatever the legs are doing. The swings and throws (`sledge_swing`, `sledge_swing_side`,
`sledge_swing_back`, `throw`, `bucket_throw`) also key the hips and legs: standing still, the knees load and the hips drive
the blow; walking or running, the legs keep moving and only the arms swing.

Hit reactions play when a blow lands on a player who lives through it (a bot, a weapon, a Sledgehammer or a blast; see
`hits` in `game/data/survival.json` for how much health makes which): `hit_front`, `hit_back`, `hit_left` and `hit_right`
flinch away from where it came from, `hit_heavy` staggers back, and `knockdown` throws the player down, where they lie a
moment and can't move, then `get_up`. A player who dies plays `death` and then lies in `downed` until they respawn; their
camera pulls back to third person to see it. Each has an `_fp` version for the first-person arms. The host sends the blow to
every peer (`PlayerHitView`), the local player's camera kicks away from it and shakes, and the screen's edge flashes red. A
character set without the reaction clips plays `hurt` instead. Every peer works out each player's pose from what it
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
The Paint Tool is
held like a pistol (`hold_paint`), raised and aimed while a drag is on (`hold_paint_aim`, from the guard byte), drawn from
the hip (`paint_draw`) and stowed back (`paint_stow`); `paint_spray` is the click's kick and `paint_reload` (the
`PaintReload` gesture, R) brings the tool round so the left hand twists the tank off and back on. The tank is a node of its
own (`paint_tool_tank`) that every peer turns with the clip (`paint_tool.tank` in `game/data/character.json`), and the
nozzles, tubes and paint (`paint_tool_tint`, `paint_tool_paint`) take the player's colour from one more byte, as does the
mist from the nozzles.

Timing lives in `game/data/character.json` (engine settings, not moddable): the speed each moving clip
is authored at, where in a cycle the feet land, crossfade times, how far the body turns before the feet
follow, and which items are held how. `motion` there sets how the body moves under the clips: how far the look turns before
the feet step round, how the upper body twists toward the look, the lean into speeding up, slowing and turning, the forward lean of a run, how much a held tool settles the arms and torso
into a carry while moving, the speed a jump becomes a leap, the idle fidgets, and how hit reactions fade. The build-up
from a walk to a sprint and the ease back down are `sprint_acceleration` and `sprint_deceleration` in `game/data/player.json`.

On uneven ground the feet are planted after the clips pose the body (`CharacterModel.Feet.cs`, `LegReach` in Core): each
foot looks for the ground under it, up to 0.5 m above or below the floor the body stands on, the hips drop for the lower
foot, each leg bends to put its foot where the clip has it but on that ground (up to 0.35 m), and a foot standing on the
ground lies along it (up to 40 degrees). It needs the `hips`, `thigh_*`, `shin_*` and `foot_*` bones every character has,
eases in and out, and lets go in the air, seated, climbing and swimming. Level ground leaves the clips as they are.

`python tools/characters/render_clips.py OUT_DIR clip ... [--view side|front|threeq|back]` renders clips straight from their
code (with the bpy module installed), and `tools/capture-animations.sh OUT_DIR [character]` records each move in a running
world as GIFs and MP4s (`CAPTURE_ONLY=walk,run_side` keeps matching moves). A mod item gets the pose of its category (`consumable` rests on
the palm and eats, `weapon` throws) unless it is listed in `item_poses`. The `eat` section of `character.json` says when the
`eat` clip's bites land (`bites`), when the food is gone (`gone`) and how long the clip runs (`seconds`); a food's held model
shows its bitten versions by them (see [Food in hand](part-data-schema.md#food-in-hand)). A drink (`properties.drink`)
is held upright in the fist (`hold_drink`, `hold_drink_fp`) and drunk with `drink` (`drink_fp` in first person): lifted,
tipped to the lips for two gulps and lowered; the `drink` section times its gulps the same way.
