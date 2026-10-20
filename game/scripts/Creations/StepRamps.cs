using Godot;
using Mechanica.Game.World;

namespace Mechanica.Game.Creations;

/// <summary>
/// Lets a Creation's round parts with a <c>collision.climb</c> (wheels) drive up a low step, a block say, instead of stalling
/// against it. A round wheel meets a block's top edge almost side on (a Scrap Wheel meets a one-block step 18° above
/// level), so a wheel the engine does not turn cannot climb it at all, and a driven one barely. Each physics tick every
/// such wheel looks ahead the way it rolls; finding a step with a level top no higher than its climb above the wheel's
/// lowest point, on something that stays put (terrain, a structure, a build anchored to the ground), it lays an
/// invisible ramp of <see cref="Slope"/> against the step's face, and Jolt rolls the wheel up it like any slope. A ramp
/// lies on <see cref="PhysicsLayers.StepRamps"/>, which only climbing wheels collide with; it goes once the wheel is up,
/// has turned away or the step is gone. Only the peer simulating the Creation lays ramps.
/// </summary>
public sealed class StepRamps
{
    /// <summary>The ramps' slope, in degrees.</summary>
    public const float Slope = 15f;

    // Steps lower than this a wheel rolls up by itself; the probe runs this high above the wheel's lowest point.
    private const float ProbeHeight = 0.08f;

    // A top this much above the climb still counts: placed blocks sit a hair proud of each other.
    private const float Slack = 0.02f;

    private const float Thickness = 0.1f, SideMargin = 0.15f, Tuck = 0.003f;

    private static readonly float Sin = MathF.Sin(Mathf.DegToRad(Slope)), Cos = MathF.Cos(Mathf.DegToRad(Slope)), Tan = Sin / Cos;

    private readonly Node3D _owner;
    private readonly Func<CreationBody, bool> _frozen;
    private readonly Func<CollisionObject3D, bool> _staysPut;
    private readonly List<Wheel> _wheels = [];
    private readonly Godot.Collections.Array<Rid> _exclude = [];

    // A climbing part: its centre and axis in its body's frame (a sphere has no axis), its radius, climb and width, and
    // the ramp it has laid, if any.
    private sealed class Wheel(CreationBody body, Vector3 centre, Vector3 axis, float radius, float climb, float width)
    {
        public CreationBody Body { get; } = body;
        public Vector3 Centre { get; } = centre;
        public Vector3 Axis { get; } = axis;
        public float Radius { get; } = radius;
        public float Climb { get; } = climb;
        public float Width { get; } = width;
        public StaticBody3D? Ramp { get; set; }
        public CollisionShape3D? Shape { get; set; }
        public Step? Laid { get; set; }
        public float Sink { get; set; }
    }

    // A step: the point of its top edge in front of the wheel, the level direction out of its face (toward the wheel) and
    // its height above the wheel's lowest point.
    private readonly record struct Step(Vector3 Edge, Vector3 Out, float Height);

    /// <param name="owner">The node the ramps hang off (top level, so they stay put in the world).</param>
    /// <param name="frozen">Whether a body is held still (not simulated here).</param>
    /// <param name="staysPut">Whether an obstacle never moves, so a ramp laid against it stays right.</param>
    public StepRamps(Node3D owner, Func<CreationBody, bool> frozen, Func<CollisionObject3D, bool> staysPut)
    {
        _owner = owner;
        _frozen = frozen;
        _staysPut = staysPut;
    }

    /// <summary>True when the Creation has a climbing part.</summary>
    public bool Any => _wheels.Count > 0;

    /// <summary>
    /// Adds a climbing part of <paramref name="body"/>: its round shape's centre and axis in the body's frame (null for a
    /// sphere), radius, climb and width along the axis, in metres. The body starts colliding with step ramps.
    /// </summary>
    public void Add(CreationBody body, Vector3 centre, Vector3? axis, float radius, float climb, float width)
    {
        _wheels.Add(new Wheel(body, centre, axis?.Normalized() ?? Vector3.Zero, radius, climb, width));
        body.CollisionMask |= PhysicsLayers.StepRamps;
    }

    /// <summary>Takes away every ramp and forgets the climbing parts (the bodies are being rebuilt).</summary>
    public void Clear()
    {
        foreach (var wheel in _wheels)
            wheel.Ramp?.QueueFree();
        _wheels.Clear();
    }

    /// <summary>The ramps laid now, for tests: each one's transform and box size.</summary>
    public IEnumerable<(Transform3D Transform, Vector3 Size)> Laid =>
        _wheels.Where(w => w.Laid is not null && w.Shape is { Disabled: false }).Select(w => (w.Ramp!.GlobalTransform, ((BoxShape3D)w.Shape!.Shape).Size));

    /// <summary>Looks ahead of every climbing wheel and lays, keeps or takes away its ramp. Call once per physics tick.</summary>
    public void Update(PhysicsDirectSpaceState3D space)
    {
        _exclude.Clear();
        foreach (var body in _wheels.Select(w => w.Body).Distinct())
            _exclude.Add(body.GetRid());
        foreach (var wheel in _wheels)
        {
            if (!GodotObject.IsInstanceValid(wheel.Body) || _frozen(wheel.Body))
            {
                Lift(wheel);
                continue;
            }
            var pose = wheel.Body.GlobalTransform;
            var centre = pose * wheel.Centre;
            var bottom = centre.Y - wheel.Radius;
            var velocity = wheel.Body.LinearVelocity + wheel.Body.AngularVelocity.Cross(centre - pose * wheel.Body.CenterOfMass);
            // The way it rolls: across a wheel's axis, or for a ball the way it moves. A wheel lying flat does not roll.
            Vector3 roll;
            if (wheel.Axis != Vector3.Zero)
            {
                var axis = pose.Basis * wheel.Axis;
                roll = axis.Cross(Vector3.Up) with { Y = 0f };
                if (MathF.Abs(axis.Normalized().Y) > 0.5f || roll.LengthSquared() < 1e-4f)
                {
                    Lift(wheel);
                    continue;
                }
            }
            else
            {
                roll = velocity with { Y = 0f };
            }
            roll = roll.LengthSquared() < 1e-6f ? Vector3.Zero : roll.Normalized();
            var along = velocity.Dot(roll);
            Step? found = null;
            if (roll != Vector3.Zero)
            {
                // Rolling, look the way it goes; standing, both ways.
                var reach = Touch(wheel, wheel.Climb) + 0.4f + MathF.Abs(along) * 0.15f;
                foreach (var dir in MathF.Abs(along) > 0.1f ? [MathF.Sign(along) * roll] : new[] { roll, -roll })
                {
                    found = Probe(space, centre, bottom, dir, reach, wheel.Climb);
                    if (found is not null)
                        break;
                }
            }
            // A wheel on its ramp keeps it until it is up, even if it sees the next step of a stair.
            if (wheel.Laid is { } laid && (found is { } again && Same(laid, again) || Keep(space, wheel, laid, centre, bottom)))
                Lay(wheel, laid, centre);
            else if (found is { } step)
                Lay(wheel, step, centre);
            else
                Lift(wheel);
        }
    }

    // How far from a step's face the wheel's centre is when it first touches the ramp up a step of this height.
    private static float Touch(Wheel wheel, float height) => (wheel.Radius / Cos - wheel.Radius + height) / Tan;

    // A step ahead along dir: a steep face no further than reach, on something that stays put, with a level top just
    // behind it no higher than climb above the wheel's lowest point.
    private Step? Probe(PhysicsDirectSpaceState3D space, Vector3 centre, float bottom, Vector3 dir, float reach, float climb)
    {
        var from = new Vector3(centre.X, bottom + ProbeHeight, centre.Z);
        var face = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + dir * reach, PhysicsLayers.Queries, _exclude));
        if (face.Count == 0 || face["collider"].AsGodotObject() is not CollisionObject3D obstacle || !_staysPut(obstacle))
            return null;
        var normal = face["normal"].AsVector3();
        var level = normal with { Y = 0f };
        if (MathF.Abs(normal.Y) > 0.3f || level.LengthSquared() < 1e-4f || level.Normalized().Dot(dir) > -0.7f)
            return null;
        var outward = level.Normalized();
        var at = face["position"].AsVector3();
        // The ground at the step's foot, and its top just behind the face.
        var foot = at + outward * 0.04f;
        var ground = space.IntersectRay(PhysicsRayQueryParameters3D.Create(foot, foot + Vector3.Down * (ProbeHeight + 0.1f), PhysicsLayers.Queries, _exclude));
        if (ground.Count == 0)
            return null;
        var floor = ground["position"].AsVector3().Y;
        var above = new Vector3(at.X, floor + climb + Slack + 0.03f, at.Z) - outward * 0.04f;
        var top = space.IntersectRay(PhysicsRayQueryParameters3D.Create(above, above with { Y = at.Y }, PhysicsLayers.Queries, _exclude));
        if (top.Count == 0 || top["normal"].AsVector3().Y < 0.9f)
            return null;
        var height = top["position"].AsVector3().Y - floor;
        return height < ProbeHeight || height > climb + Slack ? null : new Step(at with { Y = floor + height }, outward, height);
    }

    // Two sightings of one step: the same face and top.
    private static bool Same(Step a, Step b) =>
        a.Out.Dot(b.Out) > 0.995f && MathF.Abs((b.Edge - a.Edge).Dot(a.Out)) < 0.02f && MathF.Abs(b.Edge.Y - a.Edge.Y) < 0.01f;

    // The ramp's top: tucked a hair under the step's edge so the wheel meets no lip.
    private static Vector3 Corner(Step step) => step.Edge - Vector3.Up * Tuck - step.Out * Tuck;

    // A laid ramp stays while its wheel is on it or coming up to it and the step is still there.
    private bool Keep(PhysicsDirectSpaceState3D space, Wheel wheel, Step step, Vector3 centre, float bottom)
    {
        var x = (centre - step.Edge).Dot(step.Out);
        if (x < -0.05f || x > Touch(wheel, step.Height) + 0.5f || bottom > step.Edge.Y + 0.05f)
            return false;
        var inside = new PhysicsPointQueryParameters3D { Position = step.Edge - step.Out * 0.03f - Vector3.Up * 0.03f, CollisionMask = PhysicsLayers.Queries, Exclude = _exclude };
        return space.IntersectPoint(inside, 1).Count > 0;
    }

    // Lays the wheel's ramp up the step, or keeps it there. A ramp laid into the wheel would throw it off, so one laid
    // under a wheel already close to the step sinks until it just touches the wheel, leaving a lower lip at the top.
    private void Lay(Wheel wheel, Step step, Vector3 centre)
    {
        var down = step.Out * Cos - Vector3.Up * Sin;
        var normal = step.Out * Sin + Vector3.Up * Cos;
        var side = normal.Cross(down);
        var corner = Corner(step);
        // Sunk once, when laid: moving a ramp the wheel rides on would kick it.
        var kept = wheel.Laid == step && !wheel.Shape!.Disabled;
        var sink = kept ? wheel.Sink : MathF.Max(0f, wheel.Radius + Tuck - (centre - corner).Dot(normal));
        corner -= normal * sink;
        var length = step.Height / Sin + 0.15f;
        var middle = corner + down * (length / 2f) - normal * (Thickness / 2f) + side * (centre - corner).Dot(side);
        if (wheel.Ramp is null)
        {
            wheel.Shape = new CollisionShape3D { Shape = new BoxShape3D() };
            wheel.Ramp = new StaticBody3D { Name = "StepRamp", TopLevel = true, CollisionLayer = PhysicsLayers.StepRamps, CollisionMask = 0 };
            wheel.Ramp.AddChild(wheel.Shape);
            _owner.AddChild(wheel.Ramp);
        }
        // A ramp stays still unless its wheel slides well along the edge.
        if (kept && MathF.Abs((middle - wheel.Ramp.GlobalPosition).Dot(side)) < 0.1f)
            return;
        ((BoxShape3D)wheel.Shape!.Shape).Size = new Vector3(wheel.Width + 2f * SideMargin, Thickness, length);
        wheel.Ramp.GlobalTransform = new Transform3D(new Basis(side, normal, down), middle);
        wheel.Shape.Disabled = false;
        wheel.Laid = step;
        wheel.Sink = sink;
    }

    private static void Lift(Wheel wheel)
    {
        wheel.Laid = null;
        if (wheel.Shape is not null)
            wheel.Shape.Disabled = true;
    }
}
