# IO Ports

Every machine that takes items or fluids in, or sends them out, carries the same IO port face where a tube
connects. The face is a standard, not an item: it is never crafted or placed on its own, and every machine
model uses it unchanged so tubes always meet the same thing.

![The standard IO port: front, side, isometric, collar and direction variants](images/io-port.png)

## The standard

- **Size:** 3 x 3 blocks (0.75 x 0.75 m), the cross-section of a tube. The plate is 0.05 m deep and
  mounts flush on a machine face; the collar in the middle stands 17 mm proud of the plate.
- **Plate:** dark gunmetal with chamfered corners, a teal band round its edge and faint grooves on the
  0.25 m block grid.
- **Inlay:** a teal line just inside the edge that steps inward at each corner, clear of the four copper
  corner bolts.
- **Collar:** a gunmetal ring, a teal ring and a gunmetal lip round a dark bore, where the tube seats.
- **Direction:** read from the arrows. Teal arrows pointing out mark an **output**. Copper arrows pointing in
  mark an **input**. A **two-way** port carries both: teal out on its vertical axis, copper in on its
  horizontal axis.

The port's centre sits on the 0.25 m grid at the centre of a 3 x 3 block square of the machine's face, so a
machine face must be at least 3 x 3 blocks to carry one. Two ports on neighbouring faces need room
between them: a 3 x 3 x 3 machine can't carry ports on two adjacent faces because the plates would meet at
the shared edge.

Logic links made with the Connect Tool are points, not ports, and don't use this face.

## Building a machine model with ports

The geometry lives in `tools/io_port/build_io_port.py`. A machine's model script imports it and calls
`mount(parent, mode, center, normal)` with `mode` set to `"out"`, `"in"` or `"both"`, rather than drawing
its own port. `python tools/io_port/build_io_port.py --render OUT_DIR` re-renders the review sheet above.
