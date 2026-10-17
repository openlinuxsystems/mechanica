# Socket and Joint Guide

How to define sockets so parts connect the way you mean, and how the three joint kinds (hinge,
slider, spring) become real physics joints. The exact keys are in the
[Part Data Schema Reference](part-data-schema.md#sockets); this guide explains how they fit together.

## Bodies and joints

Mechanica simulates a Creation as a few rigid **Bodies** joined by a few **joints**, the way Scrap
Mechanic does:

- Everything rigidly attached merges into one Body: one rigid body with a compound collision shape, the
  parts' summed mass and their mass-weighted centre of mass. A car chassis of 200 blocks is one Body.
- A real physics joint exists only where a **joint socket** (`rotational`, `linear` or `spring`) meets
  a structural face. The part that owns the socket stays in its own Body (side A); whatever touches the
  socket starts a new Body (side B).
- The Weld Tool merges Bodies. Welding across a joint puts both sides in one Body, which locks the
  joint (the joint debug overlay draws it grey).

So a bearing on a chassis with a wheel on its rotor is two Bodies (chassis + bearing, and the wheel) and
one hinge between them.

## Sockets decide where parts attach

A socket is a point on a face of the part's footprint with an outward `normal`:

- Blocks attach on every face, whatever sockets they list. So does any part with no sockets at all.
- Any other part attaches only on faces where one of its sockets sits.
- Structural meets structural: rigid, same Body.
- A joint socket meets a structural face: a joint of the socket's kind. Two joint sockets never connect.

A bearing has a `structural` base on one face and a `rotational` rotor on the opposite face. Put it on
a chassis by its base, put a wheel on its rotor, and only the rotor face makes a hinge.

In the Part Editor's **Sockets** tab, "+ Socket on side" adds a socket centred on a face and "Snap to
face" moves a stray one back onto the nearest face. The connection preview colours every face: green
rigid, blue hinge, orange slider, purple spring, red nothing.

## The joint object

A joint socket carries a `joint` object that says how the joint moves:

```json
{ "id": "head", "type": "linear", "position": [0, 0.125, 0], "normal": [0, 1, 0],
  "joint": { "max": 1, "shaft_radius": 0.04 } }
```

- **`axis`** is the direction it turns about or slides along, in the part's own frame. It must be a grid
  direction. Omitted, it is the socket's normal: a bearing turns about its rotor face and a piston
  pushes straight out of its head. Give an axis when the motion is across the socket instead, like a
  suspension that carries a wheel on its side and moves it up and down.
- **Positions are measured from where the part was built.** When you place a Creation, both sides of
  every joint sit on the build grid: that pose is position 0. A hinge's position is the angle side B has
  turned from there (rad, right-handed about the axis: point your right thumb along the axis and your
  fingers curl the positive way). A slider's or spring's is how far side B has moved along the axis
  (m). The zero stays put when the Creation is rebuilt, so adding a block to a car with an extended
  piston doesn't reset the piston.
- **`min` and `max`** limit the position. Every range includes 0, the built pose.

The Part Editor shows these fields under the socket when its type is a joint type, and the preview
draws the joint: its axis, its range (an arc or ring for a hinge, a bar with end stops for a slider, a
zig-zag for a spring) and the part's centre of mass as a red cross. Toggle them with **Joints** above
the preview.

## Hinges (bearings)

`type: "rotational"`. With no `min`/`max` the hinge spins freely, like a wheel bearing. With both it is
a limited hinge (a door, a steering knuckle), within ±π:

```json
"joint": { "min": -0.6, "max": 0.6 }
```

Hinges are driven from Lua or the Action API, never by the part data:

- `part.set_bearing(velocity, max_torque?)` spins at a speed (rad/s). Engines and seats do this.
- `part.set_bearing_angle(angle, speed?, max_torque?)` turns to an angle and holds it (steering,
  doors). A free hinge takes the short way round.
- `nil` frees the bearing again.

The same calls exist on `creation` for a different part (`creation.set_bearing(part_id, ...)`), so one
engine can drive every wheel bearing. Limits such as the highest speed and torque are in
`game/data/motion.json`.

## Sliders (pistons)

`type: "linear"`. `max` (required) is how far the head can push out, in m; `min` defaults to 0, fully
retracted. `shaft_radius` draws a rod from the piston to the load as it extends.

- `part.set_piston(length, speed?, max_force?)` moves to a length and holds it against loads.
- `nil` frees it, so it slides between its stops (and falls back under a load).

The vanilla Piston's script (`mods/vanilla/scripts/piston.lua`) pushes out to `max` while its input
is on and pulls back to `min` when it is off. Speed and force are in its `properties`.

## Springs (suspension)

`type: "spring"`. A spring is a slider with a spring and damper instead of a motor. It needs `min`,
`max` and `stiffness`:

```json
"joint": { "axis": [0, 1, 0], "min": -0.08, "max": 0.08, "stiffness": 25000, "damping": 1500 }
```

- **`stiffness`** (N/m) sets how far a load sags it: a load of `m` kg on one spring sinks about
  `m × 9.81 / stiffness` m. A 200 kg car on four 25000 N/m springs sits 2 cm down.
- **`damping`** (N·s/m) stops it bouncing. For a corner load of `m` kg, `2 × √(stiffness × m)` is
  critical damping (no bounce at all); a third to a half of that feels like a car.
- **`min`/`max`** are the bump stops. Leave room for the sag plus the bumps you expect.

The rest length is the built pose, position 0. Under a car's weight the wheel side rides up, so the
position you see in the overlay is positive by the sag.

The vanilla suspensions mount on a chassis side by their base, carry a wheel bearing on their outer
face, and move it along their own Y:

| Part | Travel | Stiffness | Damping |
|------|--------|-----------|---------|
| Sport Suspension | ±0.08 m | 25000 N/m | 1500 N·s/m |
| Off-Road Suspension | ±0.2 m | 9000 N/m | 700 N·s/m |

To build a car: chassis blocks, a suspension on each side by its base (turned so its mount faces out),
a bearing on each suspension by its base (rotor out), a wheel on each rotor.

## Centre of mass

A part's mass sits at its footprint centre unless `centre_of_mass` moves it (m from the centre, in the
part's frame). Use it for parts whose weight is clearly off-centre, such as a wedge or a heavy base, so
lone parts and small Creations sit and tip the way they look.

## Testing joints

- **Test in Sandbox** from the Part Editor spawns the part in the Test Sandbox. Tick **Show joints**
  there to draw every joint in the world: axis and range in the joint's colour, a white marker where it
  is now, grey when it is locked, and each Body's centre of mass. With a local client added, switch to
  the client (Tab) to see the joints as the client receives them.
- The host simulates every joint (decision D5). Clients don't run joints themselves; they see the
  Bodies move through state sync, so a piston, a spinning wheel or a sagging spring looks the same on
  every machine.
- `GetCreation` over the [external AI endpoint](ai-endpoint.md) lists each joint's `position`, `min`
  and `max`.

## Troubleshooting

| Symptom | Likely cause |
|---------|--------------|
| The part sticks on rigidly where you wanted a joint | The touching face has a structural socket, or the part is a block (blocks attach rigidly on every face). |
| Nothing attaches to the joint face | The other part has sockets and none sits on the touching face. Give it a structural socket there, or use a block. |
| "a linear socket needs a joint with max" | Sliders and springs must say how far they move. |
| "axis must be a grid direction" | Use one of `[±1, 0, 0]`, `[0, ±1, 0]`, `[0, 0, ±1]`. |
| The car bottoms out or bounces | Stiffness too low for its weight (see the sag rule), or damping too low. |
| A bearing refuses `set_bearing_angle` | The angle is outside ±π or the hinge's limits. |
| The joint doesn't move at all | It is locked: both sides were welded into one Body. |
