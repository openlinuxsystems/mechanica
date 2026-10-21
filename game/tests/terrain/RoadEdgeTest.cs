using Godot;
using Mechanica.Core.World;
using Mechanica.Game.Mods;
using Mechanica.Game.World;
using Vec2 = System.Numerics.Vector2;

namespace Mechanica.Game.Tests;

/// <summary>
/// Cars drive onto the roads from the side. On the vanilla world rules it builds the chunks round a straight stretch of
/// road and a junction, as the <see cref="WorldStreamer"/> does (one ground body, a road body per chunk), and drives slow
/// cars with Scrap Wheels on motorised hinges off the shoulder onto the asphalt: head on, at a shallow angle, and across
/// a junction plate's corner. Each must roll on without stalling at the asphalt's edge or being kicked up by it.
/// The asphalt used to stand 7 cm proud of its levelled bed with a bare edge, which caught the wheels like a curb: the cars
/// lost half their speed there and were thrown up at 0.35 m/s.
/// </summary>
public partial class RoadEdgeTest : StagedTest
{
    private const float WheelRadius = 0.36f, WheelSpeed = 4f, EngineTorque = 240f, MaxKick = 0.25f, MinPace = 0.68f;

    // Ground speed of the cars on the level.
    private const float Cruise = WheelSpeed * WheelRadius;

    private readonly List<Car> _cars = [];
    private WorldGenerator _gen = null!;

    // Remaining: metres the car still has to go to be well on the road (0 or less once it is).
    private sealed record Car(string Name, RigidBody3D Chassis, Func<Vec2, float> Remaining)
    {
        public float Kick { get; set; }

        public float Slowest { get; set; } = float.MaxValue;

        public float ToGo => Remaining(new Vec2(Chassis.GlobalPosition.X, Chassis.GlobalPosition.Z));
    }

    protected override string Tag => "road-edge";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        var settings = WorldStreamer.LoadDefaultSettings();
        _gen = new WorldGenerator(WorldGenData.Load(ModPaths.LoadContent()), 20261017, settings.WorldSize, settings.Depth);
        var roads = _gen.Roads;
        var hw = roads.Definition!.Width / 2f;

        // A straight, gentle stretch on open land with level ground beside it, far from junctions and bridges.
        var (point, tangent) = Stretch(roads);
        var right = new Vec2(-tangent.Y, tangent.X);
        // A crossroads or T, and the corner between two of its arms, with nothing scattered in the corner car's way.
        var junction = roads.Nodes.Where(n => n.Kind == RoadNodeKind.Junction)
            .OrderBy(n => Level(n.At, n.Height + 0.6f, n.Radius + 8f) + (CornerBlocked(roads, n) ? 100f : 0f)).First();
        var corner = Corner(junction);

        var world = new Node3D { Name = "World" };
        AddChild(world);
        var ground = new StaticBody3D { Name = "Ground" };
        world.AddChild(ground);
        var coords = new HashSet<ChunkCoord>();
        foreach (var at in new[] { point, junction.At })
        {
            var (cx, cz) = ((int)MathF.Floor(at.X / 64f), (int)MathF.Floor(at.Y / 64f));
            for (var dz = -1; dz <= 1; dz++)
                for (var dx = -1; dx <= 1; dx++)
                    coords.Add(new ChunkCoord(cx + dx, cz + dz));
        }
        foreach (var coord in coords)
            world.AddChild(new TerrainChunk { Data = _gen.GenerateChunk(coord, 64f), State = new WorldState(20261017), Ground = ground, WorldGen = _gen.Data });

        // Each car starts on the shoulder beyond the asphalt and must get well onto it.
        // Straight cars must get their centre 2.5 m inside the asphalt's edge, the corner car 3 m onto the plate.
        float OffCentre(Vec2 p) => Vec2.Dot(p - point, right) - (hw - 2.5f);
        Spawn(world, "head on", point + right * (hw + 3.2f), -right, OffCentre);
        Spawn(world, "at a shallow angle", point + right * (hw + 2.4f) - tangent * 8f, Vec2.Normalize(-right * 0.42f + tangent), OffCentre);
        var rim = 40f - roads.OffPlate(junction, junction.At + corner * 40f);
        Spawn(world, "across a junction corner", junction.At + corner * (rim + 3.2f), -corner, p => Vec2.Dot(p - junction.At, corner) - (rim - 3f));

        Wait("cars settle on the shoulder", 30);
        Add("cars drive onto the road", frame =>
        {
            foreach (var car in _cars)
            {
                car.Kick = Mathf.Max(car.Kick, car.Chassis.LinearVelocity.Y);
                // Up to speed after the first second.
                if (frame >= 60 && car.ToGo > 0f)
                    car.Slowest = Mathf.Min(car.Slowest, (car.Chassis.LinearVelocity with { Y = 0 }).Length());
            }
            return frame >= 600 || _cars.All(c => c.ToGo <= 0f);
        });
        Add("every car is on the road and was never kicked up by its edge", _ =>
        {
            foreach (var car in _cars)
            {
                Check(car.ToGo <= 0f, $"{car.Name}: the car rolled onto the road ({MathF.Max(0f, car.ToGo):F2} m to go)");
                Check(car.Slowest > Cruise * MinPace, $"{car.Name}: slowest {car.Slowest:F2} m/s crossing the edge (> {MinPace:P0} of {Cruise:F2})");
                Check(car.Kick < MaxKick, $"{car.Name}: worst upward speed at the edge {car.Kick:F2} m/s (< {MaxKick})");
            }
            return true;
        });
    }

    // The direction from a junction's middle to the corner between its first two arms.
    private static Vec2 Corner(RoadNode junction)
    {
        var arm = junction.Arm(0);
        return Vec2.Normalize(arm + new Vec2(-arm.Y, arm.X));
    }

    // Whether a solid scattered object (a rock, a tree) stands within 4 m of the corner car's path onto the plate.
    private bool CornerBlocked(RoadNetwork roads, RoadNode junction)
    {
        var corner = Corner(junction);
        var rim = 40f - roads.OffPlate(junction, junction.At + corner * 40f);
        var (from, to) = (junction.At + corner * (rim + 4f), junction.At + corner * (rim - 4f));
        var box = (Min: Vec2.Min(from, to) - new Vec2(20f), Max: Vec2.Max(from, to) + new Vec2(20f));
        return _gen.ObjectsIn(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y).Any(o =>
        {
            if (!o.Definition.Solid)
                return false;
            var p = new Vec2(o.Position.X, o.Position.Z);
            var t = Math.Clamp(Vec2.Dot(p - from, to - from) / Vec2.DistanceSquared(from, to), 0f, 1f);
            return Vec2.Distance(p, from + (to - from) * t) < 4f + o.Definition.Footprint * o.Scale;
        });
    }

    // The worst rise or fall of the ground round a point from the given height, within the radius.
    private float Level(Vec2 at, float height, float radius)
    {
        var worst = 0f;
        for (var a = 0; a < 16; a++)
        {
            var p = at + new Vec2(MathF.Cos(a * MathF.Tau / 16f), MathF.Sin(a * MathF.Tau / 16f)) * radius;
            worst = MathF.Max(worst, MathF.Abs(_gen.HeightAt(p.X, p.Y) - height));
        }
        return worst + (_gen.HeightAt(at.X, at.Y) < _gen.SeaLevel + 1f ? 100f : 0f);
    }

    private (Vec2 Point, Vec2 Tangent) Stretch(RoadNetwork roads)
    {
        var best = (Score: float.MaxValue, Point: Vec2.Zero, Tangent: Vec2.UnitX);
        foreach (var chain in roads.Chains.Where(c => c.Kind == RoadKind.Road))
        {
            for (var i = 6; i + 6 < chain.Points.Length; i += 3)
            {
                if (chain.Bridge.Skip(i - 6).Take(13).Any(b => b))
                    continue;
                var (a, b) = (chain.Points[i - 4], chain.Points[i + 4]);
                var tangent = Vec2.Normalize(b - a);
                var bend = Vec2.Distance(chain.Points[i], (a + b) / 2f);
                var grade = MathF.Abs(chain.Heights[i + 4] - chain.Heights[i - 4]) / Vec2.Distance(a, b);
                var p = chain.Points[i];
                if (roads.Nodes.Any(n => n.Kind == RoadNodeKind.Junction && Vec2.Distance(n.At, p) < n.Radius + 30f))
                    continue;
                var right = new Vec2(-tangent.Y, tangent.X);
                var side = 0f;
                foreach (var s in new[] { -1f, 1f })
                    for (var d = 6f; d <= 10f; d += 2f)
                        side = MathF.Max(side, MathF.Abs(_gen.HeightAt(p.X + right.X * s * d, p.Y + right.Y * s * d) - chain.Heights[i]));
                var score = bend + grade * 20f + side * 4f;
                if (score < best.Score)
                    best = (score, p, tangent);
            }
        }
        return (best.Point, best.Tangent);
    }

    // A 300 kg car: a deck on four Scrap Wheels, each on a motorised hinge, heading along `forward`.
    private void Spawn(Node3D world, string name, Vec2 at, Vec2 forward, Func<Vec2, float> remaining)
    {
        var f = new Vector3(forward.X, 0, forward.Y).Normalized();
        var axle = f.Cross(Vector3.Up).Normalized();
        var y = _gen.HeightAt(at.X, at.Y) + WheelRadius + 0.05f;
        var origin = new Vector3(at.X, y, at.Y);
        var basis = new Basis(axle, Vector3.Up, -f);
        var chassis = new RigidBody3D { Name = $"Car{_cars.Count}", Mass = 260f, CanSleep = false, CollisionLayer = 2, CollisionMask = 1 };
        chassis.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.4f, 0.2f, 2.2f) } });
        chassis.Transform = new Transform3D(basis, origin + Vector3.Up * 0.25f);
        world.AddChild(chassis);
        foreach (var side in new[] { -1f, 1f })
        {
            foreach (var along in new[] { -0.9f, 0.9f })
            {
                var centre = origin + axle * side * 0.95f + f * along;
                var wheel = new RigidBody3D
                {
                    Name = $"{chassis.Name}Wheel{side}{along}",
                    Mass = 6f,
                    CanSleep = false,
                    CollisionLayer = 2,
                    CollisionMask = 1,
                    PhysicsMaterialOverride = new PhysicsMaterial { Friction = 1.05f },
                };
                // A cylinder's axis is its y: turn it onto the axle.
                wheel.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = WheelRadius, Height = 0.25f } });
                wheel.Transform = new Transform3D(new Basis(-f, axle, Vector3.Up), centre);
                world.AddChild(wheel);
                // The hinge turns about its z: lay z along the axle.
                var hinge = new HingeJoint3D { Name = $"{wheel.Name}Hinge", Transform = new Transform3D(new Basis(f, Vector3.Up, axle), centre) };
                world.AddChild(hinge);
                hinge.NodeA = chassis.GetPath();
                hinge.NodeB = wheel.GetPath();
                // Rear-wheel drive with a Scrap Gas Engine's torque; the front wheels roll free.
                if (along > 0f)
                    continue;
                hinge.SetFlag(HingeJoint3D.Flag.EnableMotor, true);
                hinge.SetParam(HingeJoint3D.Param.MotorTargetVelocity, WheelSpeed);
                hinge.SetParam(HingeJoint3D.Param.MotorMaxImpulse, EngineTorque / Engine.PhysicsTicksPerSecond);
            }
        }
        _cars.Add(new Car(name, chassis, remaining));
    }
}
