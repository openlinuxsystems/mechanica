using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Creations;

/// <summary>The aimed joint (parts map E8): a Turret Seat's two-axis turret in the Creation graph and its math.</summary>
public class AimedJointTests
{
    private const string Turret = "test:turret";
    private const string FreeTurret = "test:free_turret";

    private static SocketDefinition TurretSocket(float? min, float? max) => new()
    {
        Id = "turret",
        Type = SocketType.Aimed,
        Position = new Vec3(0, 0.125f, 0),
        Normal = new Vec3(0, 1, 0),
        Joint = new JointDefinition { Min = min, Max = max, Pivot = new Vec3(0, -0.05f, 0.05f), PitchAxis = new Vec3(1, 0, 0), PitchMin = -0.3f, PitchMax = 0.9f },
    };

    private static PartCatalog Catalog() => TestParts.Catalog()
        .Add(Turret, new PartDefinition
        {
            Id = "turret",
            Name = "Turret",
            Category = PartCategory.Interactive,
            Mass = 3,
            Size = GridSize.One,
            Sockets = [new SocketDefinition { Id = "base", Type = SocketType.Structural, Position = new Vec3(0, -0.125f, 0), Normal = new Vec3(0, -1, 0) }, TurretSocket(-1f, 1f)],
        })
        .Add(FreeTurret, new PartDefinition
        {
            Id = "free_turret",
            Name = "Free Turret",
            Category = PartCategory.Interactive,
            Mass = 3,
            Size = GridSize.One,
            Sockets = [new SocketDefinition { Id = "base", Type = SocketType.Structural, Position = new Vec3(0, -0.125f, 0), Normal = new Vec3(0, -1, 0) }, TurretSocket(null, null)],
        });

    private static (Creation Creation, JointLink Link) Built(string turret = Turret, GridRotation rotation = default)
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        creation.Place(turret, new GridCell(0, 1, 0), rotation);
        creation.Place(Block, new GridCell(0, 2, 0));
        creation.Place(Block, new GridCell(0, 3, 0));
        return (creation, Assert.Single(creation.Joints));
    }

    [Fact]
    public void WhatIsBuiltOnTheTurretIsItsOwnBodyJoinedByAnAimedJoint()
    {
        var (creation, link) = Built();

        Assert.Equal(ConnectionType.Aimed, link.Connection.Type);
        Assert.Equal("turret", link.Connection.ASocket);
        Assert.False(link.IsLocked);
        Assert.Equal(2, creation.Bodies.Count);
        Assert.Equal(2, creation.Bodies[link.BodyB].PartIds.Count);
    }

    [Fact]
    public void FrameCarriesTheYawLimitsPivotAndPitch()
    {
        var (creation, link) = Built();

        Assert.True(creation.TryGetJointFrame(link.Connection, out var frame));
        Assert.Equal(new Vec3(0, 1, 0), frame.Axis);
        Assert.Equal((-1f, 1f), (frame.Min, frame.Max));
        Assert.False(frame.SpinsFreely);
        var aim = Assert.IsType<AimFrame>(frame.Aim);
        Assert.Equal(new Vec3(1, 0, 0), aim.PitchAxis);
        Assert.Equal((-0.3f, 0.9f), (aim.PitchMin, aim.PitchMax));
        // The part's centre is at (0, 0.25, 0) m on the grid: cell (0, 1, 0).
        AssertNear(new Vec3(0, 0.25f - 0.05f, 0.05f), aim.Pivot);
        Assert.Equal(0.9f, aim.ClampPitch(2f));
        Assert.Equal(-0.3f, aim.ClampPitch(-2f));
    }

    [Fact]
    public void TurnedPartTurnsItsAxesAndPivot()
    {
        // A quarter turn about the vertical: the part's +X (its pitch axis) now points along -Z or +Z.
        var turn = GridRotation.FromAxes(new GridCell(0, 0, -1), new GridCell(0, 1, 0));
        var (creation, link) = Built(rotation: turn);

        Assert.True(creation.TryGetJointFrame(link.Connection, out var frame));
        Assert.Equal(new Vec3(0, 1, 0), frame.Axis);
        Assert.Equal(turn.Apply(new Vec3(1, 0, 0)), frame.Aim!.PitchAxis);
        AssertNear(new Vec3(0, 0.25f, 0) + turn.Apply(new Vec3(0, -0.05f, 0.05f)), frame.Aim.Pivot);
    }

    [Fact]
    public void AimFrameIsThereWithNothingBuiltOnTheTurret()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        var turret = creation.Place(FreeTurret, new GridCell(0, 1, 0)).Id;

        Assert.Empty(creation.Joints);
        Assert.True(creation.TryGetAimFrame(turret, out var frame));
        Assert.True(frame.SpinsFreely);
        Assert.Equal(ConnectionType.Aimed, frame.Type);
        Assert.False(creation.TryGetAimFrame(creation.Parts.First(p => p.Part == Block).Id, out _));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.7f, 0f)]
    [InlineData(-2.5f, 0.4f)]
    [InlineData(1.2f, -0.29f)]
    [InlineData(3f, 0.85f)]
    public void AimReadsBackTheYawAndPitchItWasTurnedTo(float yaw, float pitch)
    {
        var (creation, link) = Built(FreeTurret);
        Assert.True(creation.TryGetJointFrame(link.Connection, out var frame));
        var a = new Pose(new Vector3(4, 1, -2), Quaternion.CreateFromYawPitchRoll(0.4f, 0.1f, -0.2f));
        var b = a.Compose(JointMath.AimPose(frame, yaw, pitch));

        var (y, p) = JointMath.Aim(frame, a, b);

        Assert.Equal(yaw, y, 3);
        Assert.Equal(pitch, p, 3);
        Assert.Equal(yaw, JointMath.Position(frame, a, b), 3);
    }

    [Fact]
    public void AimPoseTurnsAboutThePivot()
    {
        var (creation, link) = Built();
        Assert.True(creation.TryGetJointFrame(link.Connection, out var frame));
        var pivot = frame.Aim!.Pivot.ToNumerics();

        var pose = JointMath.AimPose(frame, 0.8f, 0.5f);

        Assert.True(Vector3.Distance(pivot, pose.Apply(pivot)) < 1e-5f, "the pivot stays put");
        // The yaw turns about the upright axis, the pitch tilts the front (-Z) up toward it.
        var front = Vector3.Transform(-Vector3.UnitZ, pose.Rotation);
        Assert.Equal(MathF.Sin(0.5f), front.Y, 4);
        Assert.Equal(0.8f, MathF.Atan2(-front.X, -front.Z), 4);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 0.3f)]
    [InlineData(-2f, -0.2f)]
    public void AimAtFindsTheAnglesThatPointTheFront(float yaw, float pitch)
    {
        var (creation, link) = Built(FreeTurret);
        Assert.True(creation.TryGetJointFrame(link.Connection, out var frame));
        var direction = Vector3.Transform(-Vector3.UnitZ, JointMath.AimRotation(frame, yaw, pitch));

        var (y, p) = JointMath.AimAt(frame, -Vector3.UnitZ, direction);

        Assert.Equal(yaw, y, 4);
        Assert.Equal(pitch, p, 4);
    }

    [Fact]
    public void AnUpgradeMayChangeTheTurretsLimitsButNotItsSocket()
    {
        var part = Catalog().FindDefinition(Turret)! with
        {
            Upgrades =
            [
                new UpgradeDefinition
                {
                    Set = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse("""
                        { "sockets": [
                          { "id": "base", "type": "structural", "position": [0, -0.125, 0], "normal": [0, -1, 0] },
                          { "id": "turret", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0],
                            "joint": { "pivot": [0, -0.05, 0.05], "pitch_axis": [1, 0, 0], "pitch_min": -0.5, "pitch_max": 1.2 } } ] }
                        """)!,
                },
            ],
        };

        var upgraded = PartUpgrades.Apply(part, 1, out var error);

        Assert.Null(error);
        var joint = upgraded!.Sockets![1].Joint!;
        Assert.Equal((-0.5f, 1.2f), (joint.PitchMin, joint.PitchMax));
        Assert.Null(joint.Min);
    }

    [Fact]
    public void AimedJointRoundTripsThroughJson()
    {
        var part = new PartDefinition { Id = "t", Sockets = [TurretSocket(-1f, 1f)] };
        var json = DataJson.Write(part);

        Assert.Contains("\"type\": \"aimed\"", json);
        Assert.Contains("\"pitch_axis\"", json);
        Assert.Equal(json, DataJson.Write(DataJson.Read<PartDefinition>(json)!));
    }

    private static void AssertNear(Vec3 expected, Vec3 actual)
    {
        Assert.True((expected - actual).Length < 1e-4f, $"expected {expected}, got {actual}");
    }
}
