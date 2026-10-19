using Godot;
using Mechanica.Core.World;
using Mechanica.Game.World;

namespace Mechanica.Game.Tests;

/// <summary>
/// Ground chunk borders must be seamless for wheels (the aerodynamics chunk's follow-up for chunk 19). It
/// builds the four flat <see cref="TerrainChunk"/>s that meet at the origin on one shared ground body, as the
/// <see cref="WorldStreamer"/> does, and slides frictionless four-wheeled carts (vanilla wheel cylinders on
/// a deck) at 25 m/s across the borders: straight, at an angle and diagonally through the corner where all
/// four chunks meet. A cart must not be thrown up or spun when it crosses. With a body per chunk the wheels
/// caught the edge of the chunk they were leaving and jumped by 1 to 3 m/s. A fifth cart rests on the ground and
/// must stay still, and a wheel spun by a hinge motor (a bearing) must stay on its axle without wobbling. <see cref="FarChunkBorderTest"/> runs the same near the world's far corner, where positions are
/// least precise.
/// </summary>
public partial class ChunkBorderTest : StagedTest
{
    private const float Speed = 25f, WheelRadius = 0.375f;
    private const float MaxVertical = 0.25f, MaxSpin = 0.5f, MaxDrift = 0.05f;

    private readonly List<(string Name, RigidBody3D Cart, Vector3 Start)> _carts = [];
    private readonly Dictionary<RigidBody3D, (float Vertical, float Spin)> _worst = [];
    private RigidBody3D _wheel = null!;
    private Vector3 _axle;
    private float _wheelDrift, _wheelWobble;

    protected override string Tag => "chunk-border";

    /// <summary>Where the four chunks meet, in chunks from the origin along x and z.</summary>
    protected virtual int Corner => 0;

    public override void _Ready()
    {
        var world = new Node3D { Name = "World" };
        AddChild(world);
        var ground = new StaticBody3D { Name = "Ground", PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0f } };
        world.AddChild(ground);
        var generator = new WorldGenerator(WorldGenData.Flat, 1234, 12032f);
        foreach (var (x, z) in new[] { (-1, -1), (-1, 0), (0, -1), (0, 0) })
        {
            world.AddChild(new TerrainChunk
            {
                Data = generator.GenerateChunk(new ChunkCoord(Corner + x, Corner + z), 64f),
                State = new WorldState(1234),
                Ground = ground,
            });
        }

        // Each cart starts 12 m before the origin and crosses a border about half a second later.
        Spawn(world, "straight across x = 0", new Vector3(1, 0, 0), sideways: 5f);
        Spawn(world, "straight across z = 0", new Vector3(0, 0, -1), sideways: 5f);
        Spawn(world, "at an angle across x = 0", new Vector3(1, 0, 0).Rotated(Vector3.Up, 0.3f), sideways: -6f);
        Spawn(world, "diagonally through the corner", new Vector3(1, 0, 1).Normalized(), sideways: 0f);
        Spawn(world, "resting", new Vector3(1, 0, 0), sideways: 20f, speed: 0f);
        SpawnWheel(world);

        Add("the ground shapes share one body", _ =>
        {
            Check(ground.GetChildren().OfType<CollisionShape3D>().Count(s => s.Shape is HeightMapShape3D) == 4, "four height maps on the ground body");
            return true;
        });
        Wait("carts settle on the ground", 5);
        Add("carts cross the chunk borders", frame =>
        {
            foreach (var (_, cart, _) in _carts)
            {
                var (vertical, spin) = _worst.GetValueOrDefault(cart);
                _worst[cart] = (Mathf.Max(vertical, Mathf.Abs(cart.LinearVelocity.Y)), Mathf.Max(spin, cart.AngularVelocity.Length()));
            }
            _wheelDrift = Mathf.Max(_wheelDrift, _wheel.GlobalPosition.DistanceTo(_axle));
            _wheelWobble = Mathf.Max(_wheelWobble, (_wheel.AngularVelocity with { Z = 0 }).Length());
            return frame >= 60;
        });
        Add("the spinning wheel stays on its axle", _ =>
        {
            Check(Mathf.Abs(_wheel.AngularVelocity.Z) > 20f, $"the wheel spins ({_wheel.AngularVelocity.Z:F1} rad/s)");
            Check(_wheelDrift < 0.005f && _wheelWobble < 0.05f, $"worst drift off the axle {_wheelDrift * 1000:F2} mm (< 5), worst wobble {_wheelWobble:F3} rad/s (< 0.05)");
            return true;
        });
        Add("no cart was thrown or spun at a border", _ =>
        {
            foreach (var (name, cart, start) in _carts)
            {
                var (vertical, spin) = _worst[cart];
                var travelled = (cart.GlobalPosition - start) with { Y = 0 };
                if (name == "resting")
                {
                    if (!Check(travelled.Length() < MaxDrift && vertical < MaxDrift && spin < MaxDrift,
                        $"the resting cart stays still: drifted {travelled.Length():F3} m, worst vertical speed {vertical:F3} m/s, spin {spin:F3} rad/s (< {MaxDrift})"))
                        return true;
                    continue;
                }
                if (!Check(travelled.Length() > 20f, $"{name}: the cart crossed the border ({travelled.Length():F1} m)"))
                    return true;
                if (!Check(vertical < MaxVertical && spin < MaxSpin,
                    $"{name}: worst vertical speed {vertical:F2} m/s (< {MaxVertical}), worst spin {spin:F2} rad/s (< {MaxSpin})"))
                    return true;
            }
            return true;
        });
    }

    // A heavy wheel on a motorised hinge to a fixed anchor, spinning about z, clear of the ground and the carts.
    private void SpawnWheel(Node3D world)
    {
        _axle = new Vector3(Corner * 64f, 3f, Corner * 64f + 25f);
        var anchor = new StaticBody3D { Name = "Anchor", CollisionLayer = 0, CollisionMask = 0, Position = _axle };
        world.AddChild(anchor);
        _wheel = new RigidBody3D { Name = "Wheel", Mass = 200f, CanSleep = false, CollisionLayer = 4, CollisionMask = 0, Position = _axle };
        _wheel.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 1f, Height = 0.3f }, Rotation = new Vector3(Mathf.Pi / 2, 0, 0) });
        world.AddChild(_wheel);
        var hinge = new HingeJoint3D { Name = "Bearing", Position = _axle };
        world.AddChild(hinge);
        hinge.NodeA = anchor.GetPath();
        hinge.NodeB = _wheel.GetPath();
        hinge.SetFlag(HingeJoint3D.Flag.EnableMotor, true);
        hinge.SetParam(HingeJoint3D.Param.MotorTargetVelocity, 30f);
        hinge.SetParam(HingeJoint3D.Param.MotorMaxImpulse, 2000f);
    }

    private void Spawn(Node3D world, string name, Vector3 direction, float sideways, float speed = Speed)
    {
        var cart = new RigidBody3D
        {
            Name = $"Cart{_carts.Count}",
            Mass = 300f,
            CanSleep = false,
            // Carts touch only the ground, not each other.
            CollisionLayer = 2,
            CollisionMask = 1,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0f },
        };
        foreach (var x in new[] { -1f, 1f })
        {
            foreach (var z in new[] { -0.7f, 0.7f })
            {
                cart.AddChild(new CollisionShape3D
                {
                    Shape = new CylinderShape3D { Radius = WheelRadius, Height = 0.25f },
                    Rotation = new Vector3(Mathf.Pi / 2, 0, 0),
                    Position = new Vector3(x, 0, z),
                });
            }
        }
        cart.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2f, 0.25f, 1.2f) }, Position = new Vector3(0, 0.3f, 0) });
        var side = Vector3.Up.Cross(direction);
        var start = new Vector3(Corner * 64f, 0, Corner * 64f) - direction * 12f + side * sideways + new Vector3(0, WheelRadius + 0.005f, 0);
        cart.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(-direction.Z, direction.X)), start);
        cart.LinearVelocity = direction * speed;
        world.AddChild(cart);
        _carts.Add((name, cart, start));
    }
}
