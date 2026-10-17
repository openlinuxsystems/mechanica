using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Motion;

public class MotionActionsTests
{
    private static readonly ActionCaller Agent = new(1, ActionSource.External);

    private readonly FakeWorld _world = new();
    private readonly ActionDispatcher _dispatcher = new();
    private readonly Creation _car;
    private readonly int _bearing;

    public MotionActionsTests()
    {
        new MotionActions(new MotionSettings { MaxForce = 100, MaxTorque = 50, MaxDuration = 2, MaxBearingSpeed = 10, MaxBearingTorque = 40, DefaultBearingTorque = 20 }, _world)
            .Register(_dispatcher);
        _car = new Creation(7, Catalog());
        _car.Place(Block, new GridCell(0, 0, 0));
        _bearing = _car.Place(Bearing, new GridCell(0, 1, 0)).Id;
        _car.Place(Block, new GridCell(0, 2, 0));
        _car.SetAllBodyPoses(new Pose(new Vector3(1, 2, 3), Quaternion.Identity));
        _world.Creations[7] = _car;
    }

    [Fact]
    public void ApplyForcePushesTheBody()
    {
        var result = _dispatcher.Dispatch(Agent, MotionActions.ApplyForce, """{ "creation": 7, "force": [10, 0, 0], "torque": [0, 1, 0], "duration": 0.5 }""");

        Assert.True(result.Ok, result.ToString());
        Assert.Equal([(7, 0, new Vector3(10, 0, 0), new Vector3(0, 1, 0), (float?)0.5f)], _world.Pushes);
    }

    [Fact]
    public void ApplyForceOffCentreAlsoTwists()
    {
        var com = MotionActions.CentreOfMass(_car, 0);
        var at = com + new Vector3(0, 0, 2);
        var result = _dispatcher.Dispatch(Agent, MotionActions.ApplyForce,
            $$"""{ "creation": 7, "force": [10, 0, 0], "at": [{{at.X}}, {{at.Y}}, {{at.Z}}], "part": {{_car.Bodies[0].PartIds[0]}} }""");

        Assert.True(result.Ok, result.ToString());
        var (_, _, force, torque, _) = Assert.Single(_world.Pushes);
        Assert.Equal(new Vector3(10, 0, 0), force);
        // r × F = (0, 0, 2) × (10, 0, 0) = (0, 20, 0).
        Assert.Equal(0, torque.X, 3);
        Assert.Equal(20, torque.Y, 3);
        Assert.Equal(0, torque.Z, 3);
        Assert.Equal(_car.Bodies[0].PartIds[0], Assert.Single(_world.PushParts));
    }

    [Theory]
    [InlineData("""{ "creation": 7, "force": [1, 0, 0], "part": 99 }""", "no part 99")]
    [InlineData("""{ "creation": 7, "force": [1, 0, 0], "at": [500, 0, 0] }""", "most allowed is 50 m")]
    public void ApplyForceChecksPartAndPoint(string args, string reason)
    {
        var result = _dispatcher.Dispatch(Agent, MotionActions.ApplyForce, args);

        Assert.Equal(ActionErrors.Rejected, result.Code);
        Assert.Contains(reason, result.Message);
    }

    [Fact]
    public void ApplyForceRefusesAPartOfAnotherBody()
    {
        var top = _car.Bodies[^1];
        Assert.NotEqual(0, top.Index);
        var result = _dispatcher.Dispatch(Agent, MotionActions.ApplyForce, $$"""{ "creation": 7, "force": [1, 0, 0], "part": {{top.PartIds[0]}} }""");

        Assert.Contains($"is in body {top.Index}", result.Message);
    }

    [Theory]
    [InlineData("""{ "creation": 8, "force": [1, 0, 0] }""", "no creation 8")]
    [InlineData("""{ "creation": 7, "force": [1, 0, 0], "body": 5 }""", "no body 5")]
    [InlineData("""{ "creation": 7, "force": [101, 0, 0] }""", "most allowed is 100")]
    [InlineData("""{ "creation": 7, "force": [0, 0, 0], "torque": [0, 60, 0] }""", "most allowed is 50")]
    [InlineData("""{ "creation": 7, "force": [0, 0, 0], "duration": 3 }""", "at most 2")]
    [InlineData("""{ "creation": 7, "force": [0, 0, 0], "duration": 0 }""", "more than 0")]
    public void ApplyForceValidates(string args, string reason)
    {
        var result = _dispatcher.Dispatch(Agent, MotionActions.ApplyForce, args);

        Assert.Equal(ActionErrors.Rejected, result.Code);
        Assert.Contains(reason, result.Message);
        Assert.Empty(_world.Pushes);
    }

    [Fact]
    public void PlayersCannotCallMotionActions()
    {
        var result = _dispatcher.Dispatch(new ActionCaller(3, ActionSource.Player), MotionActions.ApplyForce, """{ "creation": 7, "force": [1, 0, 0] }""");

        Assert.Equal(ActionErrors.Rejected, result.Code);
        Assert.Contains("seats", result.Message);
    }

    [Fact]
    public void ParkedAndClientSimulatedCreationsCannotBeMoved()
    {
        _world.Parked.Add(7);
        Assert.Contains("parked", _dispatcher.Dispatch(Agent, MotionActions.ApplyForce, """{ "creation": 7, "force": [1, 0, 0] }""").Message);

        _world.Parked.Clear();
        _car.AuthorityPeer = 5;
        Assert.Contains("peer 5", _dispatcher.Dispatch(Agent, MotionActions.ApplyForce, """{ "creation": 7, "force": [1, 0, 0] }""").Message);
    }

    [Fact]
    public void SetBearingTargetDrivesTheBearing()
    {
        Assert.True(_dispatcher.Dispatch(Agent, MotionActions.SetBearingTarget, $$"""{ "creation": 7, "part": {{_bearing}}, "velocity": -4 }""").Ok);
        Assert.True(_dispatcher.Dispatch(Agent, MotionActions.SetBearingTarget, $$"""{ "creation": 7, "part": {{_bearing}}, "velocity": null, "max_torque": 30 }""").Ok);

        Assert.Equal([(_bearing, (float?)-4f, 20f), (_bearing, null, 30f)], _world.Motors);
    }

    [Theory]
    [InlineData(1, "velocity\": 1", "not a bearing")]
    [InlineData(99, "velocity\": 1", "no part 99")]
    [InlineData(0, "velocity\": 11", "at most 10 rad/s")]
    [InlineData(0, "velocity\": 1, \"max_torque\": 41", "at most 40")]
    public void SetBearingTargetValidates(int part, string rest, string reason)
    {
        var id = part == 0 ? _bearing : part;
        var result = _dispatcher.Dispatch(Agent, MotionActions.SetBearingTarget, $$"""{ "creation": 7, "part": {{id}}, "{{rest}} }""");

        Assert.Equal(ActionErrors.Rejected, result.Code);
        Assert.Contains(reason, result.Message);
        Assert.Empty(_world.Motors);
    }

    [Fact]
    public void GetCreationDescribesBodiesAndJoints()
    {
        var result = _dispatcher.Dispatch(Agent, MotionActions.GetCreation, """{ "creation": 7 }""");

        Assert.True(result.Ok, result.ToString());
        var state = result.Value!.Value.Deserialize<CreationState>(ActionJson.Options)!;
        Assert.Equal(7, state.Id);
        Assert.Equal([1, 2, 3], state.Parts);
        Assert.Equal(2, state.Bodies.Count);
        Assert.Equal(new Vec3(1, 2, 3), state.Bodies[0].Position);
        Assert.Equal(new Vec3(1, 0, 0), state.Bodies[0].Forward);
        var joint = Assert.Single(state.Joints);
        Assert.Equal((_bearing, ConnectionType.Rotational, false), (joint.Part, joint.Type, joint.Locked));
        Assert.Equal("rotational", result.Value!.Value.GetProperty("joints")[0].GetProperty("type").GetString());
    }

    [Fact]
    public void QueriesAreReadOnly()
    {
        Assert.True(_dispatcher.IsReadOnly(MotionActions.GetCreation));
        Assert.True(_dispatcher.IsReadOnly(MotionActions.ListCreations));
        Assert.False(_dispatcher.IsReadOnly(MotionActions.ApplyForce));
        var list = _dispatcher.Dispatch(Agent, MotionActions.ListCreations, "{}");
        Assert.Equal(7, list.Value!.Value[0].GetProperty("id").GetInt32());
        Assert.Equal(3, list.Value!.Value[0].GetProperty("parts").GetInt32());
    }

    [Fact]
    public void SettingsLoadFromJsonAndReject()
    {
        var settings = MotionSettings.FromJson("""{ "max_force": 5, /* N */ "default_bearing_torque": 1, }""");
        Assert.Equal(5, settings.MaxForce);
        Assert.Throws<FormatException>(() => MotionSettings.FromJson("""{ "max_force": -1 }"""));
        Assert.Throws<FormatException>(() => MotionSettings.FromJson("""{ "max_bearing_torque": 1, "default_bearing_torque": 2 }"""));
        Assert.Throws<FormatException>(() => MotionSettings.FromJson("""{ "speed": 1 }"""));
    }

    private sealed class FakeWorld : IMotionWorld
    {
        public Dictionary<int, Creation> Creations { get; } = [];
        public HashSet<int> Parked { get; } = [];
        public List<(int, int, Vector3, Vector3, float?)> Pushes { get; } = [];
        public List<int?> PushParts { get; } = [];
        public List<(int, float?, float)> Motors { get; } = [];

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public IEnumerable<Creation> AllCreations => Creations.Values;

        public bool IsLive(int id) => !Parked.Contains(id);

        public void ApplyForce(Creation creation, int body, Vector3 force, Vector3 torque, float? duration, int? part)
        {
            Pushes.Add((creation.Id, body, force, torque, duration));
            PushParts.Add(part);
        }

        public List<JointMotor> JointMotors { get; } = [];

        public Dictionary<int, float> Positions { get; } = [];

        public void SetJointMotor(Creation creation, JointLink joint, JointMotor motor)
        {
            Motors.Add((joint.Connection.A, motor.Velocity, motor.MaxEffort));
            JointMotors.Add(motor);
        }

        public float? JointPosition(Creation creation, JointLink joint) =>
            Positions.TryGetValue(joint.Connection.A, out var p) ? p : null;
    }
}
