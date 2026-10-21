# IO Ports

Every machine that takes items or fluids in, or sends them out, carries the same IO port face where a tube
connects. The face is a standard, not an item: it is never crafted or placed on its own, and every machine
model uses it unchanged so tubes always meet the same thing.

![The standard IO port: front, side, isometric, collar and direction variants](images/io-port.png)

## The standard

- **Size:** 2 x 2 blocks (0.5 x 0.5 m). The plate is 33 mm deep and mounts flush on a machine face; the
  collar in the middle stands 11 mm proud of the plate. (It was 3 x 3 until 2026-10-20 and shrank, same
  design, so it fits the ends of the 4 x 3 x 3 Gas and Battery Containers and future chests.)
- **Plate:** dark gunmetal with chamfered corners, a teal band round its edge and faint grooves
  across the face.
- **Inlay:** a teal line just inside the edge that steps inward at each corner, clear of the four copper
  corner bolts.
- **Collar:** a gunmetal ring, a teal ring and a gunmetal lip round a dark bore, where the tube seats.
- **Direction:** read from the arrows. Teal arrows pointing out mark an **output**. Copper arrows pointing in
  mark an **input**. A **two-way** port carries both: teal out on its vertical axis, copper in on its
  horizontal axis.

### Placement

Machines connect to each other and to tubes, so every machine puts its ports in the same place:

- **Floor level, same height everywhere.** The port's centre is 0.375 m above the floor the machine stands
  on: the middle of a 3-block-tall container's end. Machines, containers and chests standing side by side on
  one floor have their ports level with each other.
- **Centred on a block.** The port's centre sits on the centre of a block of the 0.25 m grid. Along the face,
  the port is centred; when the face is an even number of blocks wide, it moves half a block toward the
  machine's front.
- **Flush with the footprint.** The collar's face lies exactly on the machine's footprint boundary. Nothing
  pokes outside the footprint, and the plate sits 11 mm inside it, so the wall behind the plate is set in by
  at least that much.

A face must be at least 3 blocks wide and 3 blocks tall from the floor to carry a port.

Containers follow the same rule. The Small Chest (one port), Large Chest (three) and Cargo Chest (ten: three on the
front, three on the back, two on each end) carry ports, as do the Gas, Battery, Water, Chemical and Potato Ammo
Containers (one on the right end each). The Scrap Chest is too short for a port, and furniture and world containers
(Fridge, Locker, File Cabinet, Tool Cabinet, Barrel, crates, Dispenser, Broken Microwave, Ruin Chest) have none.

Logic links made with the Connect Tool are points, not ports, and don't use this face.

## Building a machine model with ports

The geometry lives in `tools/io_port/build_io_port.py`. A machine's model script imports it and calls
`mount(parent, mode, center, normal)` with `mode` set to `"out"`, `"in"` or `"both"` and `center` the port's
centre on the footprint boundary, rather than drawing its own port. `python tools/io_port/build_io_port.py --render OUT_DIR` re-renders the review sheet above.
