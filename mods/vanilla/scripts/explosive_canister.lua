-- Explosive Canister (parts map batch 6; the Large and the Small one share it). It goes off:
-- * at once when it breaks (any damage that finishes it), when another blast reaches it (a chain of canisters goes off
--   together), or when it hits something at properties.impact_speed m/s or more (dropped from high, rammed, crashed);
-- * properties.fuse seconds after any other damage (a shot, a Sledgehammer, a saw, fire): the fuse hisses and its
--   lamp blinks red.
-- The blast (combat.explode) reaches properties.radius m, dealing properties.damage at its middle and falling off to 0
-- at the edge, pushing parts away with up to properties.impulse N·s; flammable parts within properties.burn_radius m
-- catch fire at properties.burn strength. The canister breaks with it.
-- Sounds: "fuse" when the fuse is lit, "explode" for the blast. Its model plays "fuse_loop" on its "pose" readout
-- while the fuse burns.

local function blast(at)
  if state.done then
    return
  end
  state.done = true
  at = at or part.position()
  if not at then
    return
  end
  local id = part.sound("explode")
  if id then
    sound.play(id, at)
  end
  local hits = combat.explode(at, properties.radius, properties.damage, properties.impulse)
  for _, hit in ipairs(hits) do
    if hit.kind == "part" and hit.creation and hit.part and (hit.position - at):length() <= properties.burn_radius
        and not (hit.creation == part.creation() and hit.part == part.id()) then
      fire.ignite(hit.creation, hit.part, properties.burn)
    end
  end
  part.destroy("explosion")
end

local function light()
  if state.done or state.fuse_at then
    return
  end
  state.fuse_at = world.time() + properties.fuse
  local id = part.sound("fuse")
  if id then
    sound.play(id)
  end
  part.display("pose", "fuse_loop")
end

function on_create()
  if state.fuse_at then
    part.display("pose", "fuse_loop")
  end
end

function on_damage(amount, kind, broken, at)
  if broken or kind == "explosion" then
    blast(at)
  else
    light()
  end
end

function on_collision(other, impulse)
  local mass = part.body_mass()
  if mass and mass > 0 and impulse / mass >= properties.impact_speed then
    blast(part.position())
  end
end

function on_update(dt)
  if state.fuse_at and not state.done and world.time() >= state.fuse_at then
    blast(part.position())
  end
end
