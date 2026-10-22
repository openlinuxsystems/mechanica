using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Motion;

/// <summary><c>SetAimTarget</c>: a Turret Seat's script (or an agent) aims its turret; the host's world drives it (parts map E8).</summary>
public class AimActionsTests
{
    private static readonly ActionCaller Agent = new(1, ActionSource.External);
    private static readonly ActionCaller Player = new(2, ActionSource.Player);

    private readonly FakeWorld _world = new();
    private readonly ActionDispatcher _dispatcher = new();
    private readonly int _turret;
    private readonly int _bare;

    public AimActionsTests()
    {
        new MotionActions(new MotionSettings { MaxBearingSpeed = 10, MaxBearingTorque = 40, DefaultBearingTorque = 20, DefaultBearingSpeed = 3 }, _world)
            .Register(_dispatcher);
        var catalog = Catalog().Add("test:turret", new PartDefinition
        {
            Id = "turret",
            Name = "Turret",
            Category = PartCategory.Interactive,
            Mass = 3,
            Size = GridSize.One,
            Sockets =
            [
                new SocketDefinition { Id = "base", Type = SocketType.Structural, Position = new Vec3(0, -0.125f, 0), Normal = new Vec3(0, -1, 0) },
                new SocketDefinition
                {
                    Id = "turret", Type = SocketType.Aimed, Position = new Vec3(0, 0.125f, 0), Normal = new Vec3(0, 1, 0),
                    Joint = new JointDefinition { Min = -1, Max = 1, PitchAxis = new Vec3(1, 0, 0), PitchMin = -0.2f, PitchMax = 0.6f },
                },
            ],
        });
        var creation = new Creation(7, catalog);
        creation.Place(Block, new GridCell(0, 0, 0));
        _turret = creation.Place("test:turret", new GridCell(0, 1, 0)).Id;
        creation.Place(Block, new GridCell(0, 2, 0));
        creation.Place(Block, new GridCell(1, 0, 0));
        _bare = creation.Place("test:turret", new GridCell(1, 1, 0)).Id;
        _world.Creations[7] = creation;
    }

    [Fact]
    public void SetAimTargetDrivesTheTurret()
    {
        var result = _dispatcher.Dispatch(Agent, MotionActions.SetAimTarget, $$"""{ "creation": 7, "part": {{_turret}}, "yaw": 0.5, "pitch": -0.1, "speed": 2 }""");

        Assert.True(result.Ok, result.Message);
        var (part, motor) = Assert.Single(_world.Aims);
        Assert.Equal(_turret, part);
        Assert.Equal(new AimMotor(0.5f, -0.1f, 2f, 20f), motor);
        Assert.Equal(new JointMotor(2f, 20f, 0.5f), motor.YawMotor);
        Assert.Equal(new JointMotor(2f, 20f, -0.1f), motor.PitchMotor);
    }

    [Theory]
    [InlineData("\"yaw\": 1.5, \"pitch\": 0", "outside turret")]
    [InlineData("\"yaw\": 0, \"pitch\": 0.7", "outside turret")]
    [InlineData("\"yaw\": 0, \"pitch\": -0.3", "outside turret")]
    [InlineData("\"yaw\": 4, \"pitch\": 0", "within ±π")]
    [InlineData("\"yaw\": 0, \"pitch\": 0, \"speed\": 11", "at most 10 rad/s")]
    [InlineData("\"yaw\": 0, \"pitch\": 0, \"speed\": 0", "more than 0")]
    [InlineData("\"yaw\": 0, \"pitch\": 0, \"max_torque\": 41", "at most 40")]
    public void SetAimTargetValidates(string rest, string reason)
    {
        var result = _dispatcher.Dispatch(Agent, MotionActions.SetAimTarget, $$"""{ "creation": 7, "part": {{_turret}}, {{rest}} }""");

        Assert.Equal(ActionErrors.Rejected, result.Code);
        Assert.Contains(reason, result.Message);
        Assert.Empty(_world.Aims);
    }

    [Fact]
    public void ATurretWithNothingOnItCannotBeAimed()
    {
        var result = _dispatcher.Dispatch(Agent, MotionActions.SetAimTarget, $$"""{ "creation": 7, "part": {{_bare}}, "yaw": 0, "pitch": 0 }""");

        Assert.Contains("not a turret with something built on it", result.Message);
    }

    [Fact]
    public void PlayersAimThroughTheSeatNotTheAction()
    {
        var result = _dispatcher.Dispatch(Player, MotionActions.SetAimTarget, $$"""{ "creation": 7, "part": {{_turret}}, "yaw": 0, "pitch": 0 }""");

        Assert.False(result.Ok);
        Assert.Contains("seats", result.Message);
    }

    [Fact]
    public void GetCreationReportsTheTurretsYawAndPitch()
    {
        _world.AimAt[_turret] = (0.25f, 0.1f);
        var state = new MotionActions(new MotionSettings(), _world).Describe(_world.Creations[7]);

        var joint = Assert.Single(state.Joints);
        Assert.Equal(ConnectionType.Aimed, joint.Type);
        Assert.Equal((0.25f, 0.1f), (joint.Position, joint.Pitch));
        Assert.Equal((-1f, 1f, -0.2f, 0.6f), (joint.Min, joint.Max, joint.PitchMin, joint.PitchMax));
    }

    private sealed class FakeWorld : IMotionWorld
    {
        public Dictionary<int, Creation> Creations { get; } = [];
        public List<(int Part, AimMotor Motor)> Aims { get; } = [];
        public Dictionary<int, (float Yaw, float Pitch)> AimAt { get; } = [];

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public IEnumerable<Creation> AllCreations => Creations.Values;

        public bool IsLive(int id) => true;

        public void ApplyForce(Creation creation, int body, Vector3 force, Vector3 torque, float? duration, int? part, Vector3? at, Vector3 ownTorque)
        {
        }

        public void SetJointMotor(Creation creation, JointLink joint, JointMotor motor)
        {
        }

        public float? JointPosition(Creation creation, JointLink joint) => null;

        public void SetAimMotor(Creation creation, JointLink joint, AimMotor motor) => Aims.Add((joint.Connection.A, motor));

        public (float Yaw, float Pitch)? AimPosition(Creation creation, JointLink joint) =>
            AimAt.TryGetValue(joint.Connection.A, out var at) ? at : null;
    }
}
