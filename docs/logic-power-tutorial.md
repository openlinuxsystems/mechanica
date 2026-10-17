# Logic and Power Tutorial

Seats, engines, switches and timers talk to each other over the **logic bus**. You wire them with
the **Connect Tool**, watch the wires with the **Logic Visualizer**, and look inside a creation with
the **Creation Inspector**. This tutorial builds a drivable car, then a switch that works a piston, and
ends with what a modder needs to make new logic parts.

## How signals work

- Every logic part has one **output**: a number, 0 when it is off. A switch outputs 1 while it is on,
  a driver's seat outputs its throttle from -1 (back) to 1 (forward).
- A **link** goes from one part's output to another part's input, inside one creation. A part's
  **input** is the sum of the outputs of every part linked to it.
- A signal moves one link per physics step (1/60 s). A switch that feeds a timer that feeds a piston
  reaches the piston two steps after you flip it.
- How many links a part takes and sends is in its data. A seat sends up to 10, an engine takes up to
  4 and sends up to 10, a bearing or a piston takes up to 4.
- What a signal means is up to the part that receives it. An engine reads its input as a throttle, a
  piston pushes out while its input is not 0, and a logic gate counts every input that is not 0 as on.
- Links are saved with the creation and copied with it. Taking a part off removes its links, and when
  a creation breaks in two, a link between the two pieces is lost.

## The tools

| Key | What it does |
|-----|--------------|
| 7 | Takes the **Connect Tool** in hand. While it is in hand you see every link nearby. |
| Left click (Connect Tool) | Picks the part under the crosshair as the source. Left click another part of the same creation to link the source to it, or click a part it already feeds to take that link away. The source stays picked, so you can feed several parts in a row. Clicking the source again lets go. |
| Right click (Connect Tool) | Lets go of the source. |
| R (Connect Tool) | Opens the settings of the part under the crosshair (a gate's mode, a timer's delay). |
| F7 | Turns the **Logic Visualizer** on or off. Links are drawn from the source to an arrowhead at the part they feed: yellow while the source is on, blue while it is off. |
| F8 | Opens the **Creation Inspector** for the creation under the crosshair: its mass, bodies, joints, links, and for each part its output, input, settings, who sits in it and whether its script runs or stopped on an error. |
| F9 | Turns the **force overlay** on or off: an arrow for every push a part script makes, such as a thruster's thrust or a wing's lift (see the [Aero and Propulsion Tutorial](aero-tutorial.md)). |
| F | Uses what you look at: sit in a seat, flip a switch, press a button. While seated, F stands you up. |

Every change goes to the host, which checks it (you must be within reach of both parts, and both ends
must have a free link) and sends the result to everyone. The visualizer and the inspector read the
host's signals, so a client sees exactly what the host simulates.

## Build a car

You need a driver's seat, a gas engine, five bearings, four wheels and some metal blocks. In Survival
you also need a gas container with gasoline in it; in Creative engines run without fuel.

1. **Chassis.** Lay a row of five metal blocks. The car will drive along the row.
2. **Seat and engine.** Put the driver's seat and the engine on top of the chassis, both facing the
   same way. An engine rolls its wheels toward its front (the side with the small block on it), and the
   seat faces the same way when its back is behind you.
3. **Rear wheels.** Under the back block hang two more blocks, put a bearing on each side of the lower
   one with its rotor facing out, and put a wheel on each rotor by its hub.
4. **Steering.** Under the front block put a bearing with its rotor facing down, a block under the
   rotor (the axle), and a bearing and a wheel on each side of the axle as at the back. The upright
   bearing is the steering bearing.
5. **Wire it.** Take the Connect Tool. Click the seat, then the engine, then the steering bearing: the
   seat now feeds both. Click the engine, then each of the four wheel bearings.
6. **Gear.** Aim at the engine and press R. Gear 1 is slow and easy to control; gear 5 is the top
   speed (`speed` in the engine's data). Each gear is a fifth of it.
7. **Drive.** Look at the seat and press F. W and S are the throttle, A and D steer. With no throttle
   the engine holds the wheels (its `brake`), so the car does not roll away. F stands you up.

If the car drives backwards, the engine faces the wrong way: turn it round. If it steers the wrong way,
the steering bearing is at the back. A seat only steers bearings that turn about an upright axis, and an
engine only drives bearings whose axis is across its front, so linking the seat to a wheel bearing or the
engine to the steering bearing does nothing.

### Fuel

A gas engine burns one gasoline from the creation's gas containers for every 40 s of full throttle
(half throttle lasts twice as long). When the last one is gone it sputters and stops until you fill a
container again. The electric engine draws battery packs from batteries the same way. The Creation
Inspector shows an engine's input, so you can tell an empty tank from a broken wire.

## A switch and a piston

1. Put a switch and a piston on a block, and something on the piston's head.
2. With the Connect Tool, click the switch, then the piston.
3. Look at the switch and press F. The piston pushes out to the end of its stroke and holds there.
   Press F again and it pulls back.

Put a **timer** between them (switch → timer → piston) and set its delay with R: the piston moves that
many seconds after the switch. Use a **button** instead of the switch for a short pulse, or a **logic
gate** to need two switches at once.

## The vanilla logic parts

Settings are changed with R and the Connect Tool; properties are fixed in the part's data.

| Part | Output | Settings | Properties |
|------|--------|----------|------------|
| Driver's Seat, Scrap Driver's Seat | The driver's throttle, -1 to 1 | | `steer_angle` (°), `steer_speed` (rad/s), `steer_torque` (N·m) |
| Passenger Seat, Scrap Seat | (none) | | |
| Gas Engine, Scrap Gas Engine, Electric Engine | Its throttle while it runs | `gear` 1 to 5 | `speed` (rad/s in top gear), `torque` and `brake` (N·m), `fuel` (item), `fuel_time` (s per item), `loop_parameter` |
| Logic Gate | 1 or 0 | `mode`: and, or, xor, nand, nor, xnor | |
| Timer | Its input, `delay` seconds later | `delay` (s) | |
| Sensor | 1 while something is within `range` of its face | `range` (m) | `interval` (steps between checks) |
| Switch | 1 while on; F toggles it | | |
| Button | 1 for `pulse` seconds after F | | `pulse` (s) |
| Controller | 1 while it runs | `frames` (angles in °, up to 10), `frame_time` (s), `loop` | `speed` (rad/s), `torque` (N·m) |

The **controller** steps the bearings it is linked to through its list of angles while its input is
on: each bearing turns to the next angle every `frame_time` seconds, and starts over when `loop` is on.
Every bearing it feeds goes to the same angle. It is how you make doors, arms and walking legs.

A logic gate counts each input that is not 0 as on. With no inputs everything is off, so nand, nor and
xnor are on. xor is on when an odd number of inputs are on.

## Making logic parts

A logic part is ordinary part data plus a script (decision D2: the behaviour is Lua, the game only
moves the signals). Start from the **logic**, **seat**, **engine** or **wheel** template in the Part
Editor. The keys are in the [Part Data Schema Reference](part-data-schema.md#logic-settings-and-seats):

```json
"script": "scripts/timer.lua",
"logic": { "inputs": 10, "outputs": 10 },
"settings": [
  { "id": "delay", "name": "Delay", "type": "number", "min": 0, "max": 60, "step": 0.05, "default": 1 }
]
```

The script reads its input and sets its output (see the [Lua API Reference](lua-api.md)):

| Lua | What it does |
|-----|--------------|
| `on_power(amount)` | Runs when the sum of the part's inputs changes. |
| `get_outputs()` | Returns the part's output each step (a number, or a boolean as 1 or 0). |
| `part.set_output(value)` | Sets the output directly instead. |
| `part.input()`, `part.inputs()` | The sum of the inputs, or each one as `{part, value}`. |
| `part.links()` | The parts this part feeds (an engine's wheel bearings). |
| `part.setting(id)` | The value of one of the part's settings. |
| `on_activate(peer)` | A player pressed F on the part (give it `logic.use` so F offers it). |
| `part.occupant()`, `part.occupant_input()` | For a seat: who sits in it and what they press. |
| `creation.take(item, count)` | Burns fuel from the creation's containers. |
| `world.creative()` | True in Creative, where engines need no fuel. |

The vanilla scripts in `mods/vanilla/scripts/` (`seat.lua`, `engine.lua`, `logic_gate.lua`,
`timer.lua`, `sensor.lua`, `switch.lua`, `button.lua`, `controller.lua`) are short and make good starting
points.

### Wheels

A wheel is a part with one structural socket, its hub, and a cylinder collision along its local Y. Put
it on a bearing's rotor by the hub and it turns with the bearing. Its `friction` is its grip. The vanilla
wheels are 3 × 1 × 3 cells (the Big Wheel 5 × 2 × 5); the Hammer centres a wide part on the face you aim
at.

### Directions

A placed part's front is its local -Z and its top its local +Y. An engine works out the sign for each
wheel bearing from the bearing's axis: a positive bearing velocity turns right-handed about the axis
(see the [Socket and Joint Guide](socket-joint-guide.md)), so a wheel on the right of a car rolls it
forward with a negative velocity and one on the left with a positive one. A seat steers right by
turning its upright bearings clockwise seen from above.
