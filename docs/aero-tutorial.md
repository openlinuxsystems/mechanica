# Aero and Propulsion Tutorial

Thrusters, wings, rotors, control surfaces and hover jets make creations fly. Every one of them is a
Lua script on a vanilla part (`mods/vanilla/scripts/`), so a mod can change how they behave or add new
ones. This tutorial builds a plane and a helicopter, shows how to see the forces, and ends with the two
example mods that show a modder how to make a motor bearing and a thruster.

Read the [Logic and Power Tutorial](logic-power-tutorial.md) first: aero parts are wired with the same
Connect Tool and listen to the same seats and switches.

## How flying works

- Every aero part pushes its own creation with a force at the part, in newtons, once per physics step
  (1/60 s). A push off the centre of mass also turns the creation, so where you put a part matters as
  much as how strong it is.
- The host runs the scripts and the physics. Clients see the result, so a plane flies the same for
  everyone.
- There is no wind. The air is still, and a wing feels the air its own motion makes.
- Mass is what you fight. A creation's weight is its mass times 9.81 N; the Creation Inspector (F8)
  shows its mass.

## The parts

| Part | Size (cells) | What it does | Numbers |
|------|--------------|--------------|---------|
| Thruster | 1×1×1 | Pushes its creation toward its base: the flame comes out of the face opposite the one you attach. | 200 N at full power; burns a gasoline every 112 s at full power (0.5 fuel a second, none in Creative). |
| Wing | 4×1×4 (1 m by 1 m) | Lift square to the air flowing over it, and drag along it. It is cambered: it lifts toward its top even when level. It stalls past about 10° up. | Lift coefficient 0.4 level, plus 4.5 per radian; at most 1.2. Area 1 m². |
| Control Surface | 4×1×2 (1 m by 0.5 m) | A wing without camber that tilts the air it meets by up to 10° on command: an elevator, aileron or rudder. | Area 0.5 m². |
| Rotor | 3×1×3 | A propeller that pulls its creation away from its base, and twists the creation the other way from its spin. It spools up and down. | 150 N and 40 N·m at full speed; full speed in 1.25 s. |
| Hover Jet | 2×1×2 | Like a thruster, and while "Stabilize" is on it keeps its body level (never turning it about the vertical). | 300 N; levelling up to 600 N·m. |

The numbers are the parts' `properties` in `mods/vanilla/editor/batches/aero.json`; see
[Part Data Schema Reference](part-data-schema.md#aero-parts) for each one.

Wing lift is ½ · 1.225 kg/m³ · speed² · area · lift coefficient. A level wing at 20 m/s lifts about
98 N; at 30 m/s about 220 N. A rotor's thrust drops as it climbs along its axis (none at 10 m/s) and
grows as it sinks (up to 1.3 times), so a helicopter settles into a gentle climb or a hover instead of
racing away.

## Controls

Link a seat to an aero part with the Connect Tool, then aim at the part and press **R** to pick its
**Control**:

| Control | Command | Default on |
|---------|---------|------------|
| Link | The sum of its logic inputs (a switch, a timer, a gate). | Thruster, Wing, Rotor, Hover Jet |
| W / S | The pilot's W (1) and S (-1). | Control Surface |
| A / D | The pilot's D (1) and A (-1). | |
| Space / Shift | A lever: Space raises it, Shift lowers it, and it stays where you leave it. Thrusters, rotors and jets go from 0 to full in 2 s; a control surface goes from -1 to 1. | |

**Invert** flips the command, which is how a left aileron moves opposite the right one, or how S
pushes a thruster that W does not. Thrusters, rotors and jets ignore negative commands.

While seated, F stands you up, so Space and Shift are free for the lever.

## See the forces

Press **F9** to show the force overlay (the Test Sandbox has a "Show forces" box). For every creation
within 60 m it draws each push the host applied in its last step:

- a **cyan arrow** from the part along the force, 1 m per 100 N (at most 6 m);
- a **violet line** along a twist, such as a rotor's counter-torque, 1 m per 50 N·m.

A plane in level flight shows thruster arrows pointing forward, wing arrows pointing mostly up and
leaning back (the drag), and small control surface arrows that swing as you press W and S. A client
sees the host's arrows up to a tenth of a second late.

## Orientation

- A wing's surface is its flat face. "Top" is the face away from the bottom `mount` socket. A wing attaches by its root edge (`root_1` to
  `root_4`, its -X side) to the body, and another wing goes on its tip (+X).
- A wing on the other side of the body is the same part turned 180° about the vertical (press the
  rotate key twice before placing it), so its root faces the body and its top stays up.
- A control surface hinges on its long edge (`hinge_1` to `hinge_4`), which goes against the trailing
  edge of a wing or a block. Turned on its side it is a rudder.
- Thrusters and hover jets push toward the face you attach them by. A thruster on the back of a
  plane, attached by its base to the tail, pushes the plane forward.
- A rotor pulls away from the face you attach it by: put it on top of the body.

## Build a plane

This is the plane the automated test flies (`game/tests/vanilla/VanillaAeroTest.cs`); it weighs about
94 kg. Cell coordinates are in 0.25 m cells, with the nose at z = 0 and the tail at z = 8.

1. **Fuselage.** A row of nine cardboard blocks from the nose to the tail, with a metal block under
   the nose block as ballast. A plane flies nose first only if its centre of mass is ahead of its
   wings.
2. **Seat.** A driver's seat on the second block, facing the nose.
3. **Wings.** On each side, two wings end to end, the first by its root against the fourth block. Keep
   the wings behind the centre of mass, or the plane will flip.
4. **Elevators.** A control surface on each side of the tail, hinged to the fuselage, and a third
   standing upright on top of the fuselage, just behind the wings, as a fin.
5. **Thrusters.** Four thrusters stacked on the tail, attached by their bases. Keep the thrust line
   level with the centre of mass, or full throttle pitches the plane up or down.
6. **Wheels.** Two legs of two blocks under the fuselage, one near the nose and one behind the wings,
   each with a bearing on both sides and a wheel on each bearing.
7. **Wire it.** With the Connect Tool, link the seat to each thruster and set their Control to
   Space / Shift. Link the seat to both elevators (they follow W / S already); if one moves the wrong
   way, set its Invert.
8. **Fly.** Sit down, hold Space for about three seconds (full throttle), and roll down the runway.
   Once it rolls faster than about 15 m/s, hold S to lift the nose, and let go once it climbs. W
   pushes the nose down.

## Build a helicopter

About 43 kg; it climbs on four rotors and the hover jet keeps it level.

1. **Body.** A cross of cardboard blocks, three blocks to each side of the middle, and a seat in the
   middle.
2. **Rotors.** One rotor on top of the end of each arm. Set the two on the front and back arms to
   spin counterclockwise (R): a clockwise and a counterclockwise pair cancel each other's twist, so the
   helicopter does not turn.
3. **Hover jet.** One hover jet under the middle, attached by its base, with Stabilize on.
4. **Wire it.** Link the seat to the four rotors and the jet, and set every one to Space / Shift.
5. **Fly.** Hold Space for a second and a half. It lifts off and climbs, and the climb slows by itself
   into a hover about 15 m up. A half-second tap of Shift lets it sink gently; a tap of Space holds it.

## Making aero parts

Aero parts are ordinary part scripts using generic primitives (see [Lua API Reference](lua-api.md)):

| Call | What an aero script uses it for |
|------|---------------------------------|
| `part.velocity()` | The air speed: there is no wind, so the air flows at minus the part's velocity. |
| `part.to_world(v)` | The part's own axes in the world: its surface normal, its thrust axis. |
| `part.apply_force(force, at)` | Lift, drag and thrust at the part. |
| `part.apply_torque(torque)` | A rotor's counter-torque, a hover jet's levelling. |
| `part.input()`, `part.inputs()`, `creation.occupant_input(seat)` | The command, from links or a pilot. |
| `part.setting(id)` | The player's choices (Control, Invert, Spin). |
| `sound.start`, `sound.set_parameter` | A loop that follows the speed or the power. |

Balance numbers go in the part's `properties`, never in the script, so a mod can retune a wing without
touching its Lua. Every vanilla aero script clamps what it is given, because a player can link ten
switches to one thruster.

### Example mods

Two small mods in `examples/mods/` show the whole loop, each a commented Lua script and a Part Editor
batch:

- **[example_bearing](../examples/mods/example_bearing)**: a Motor Bearing that spins at 2 rad/s per
  unit of its input with `part.set_bearing`. Link a switch to it, put a wheel on its rotor, and it
  drives.
- **[example_thruster](../examples/mods/example_thruster)**: a Toy Thruster that pushes toward its base
  with 50 N per unit of its input, capped at two units. It is the vanilla thruster with the controls and
  fuel left out.

To try one, copy its folder into your `mods/` folder and enable it in the Mod Manager. To change one,
edit its `editor/batches/parts.json` and re-run `tools/author-vanilla.sh` (or open the part in the Part
Editor); edit its script in the Part Editor's Lua panel or any text editor.

### What it costs

Aero scripts run on the host every physics step. On a 4-core build machine (`tools/lua-bench.sh`), one
vanilla aero part costs 18 to 30 µs a step: 200 wings take 4.2 ms of a 16.7 ms frame. The plane above
(eleven aero parts) costs about 0.25 ms a step, the helicopter about 0.13 ms.
