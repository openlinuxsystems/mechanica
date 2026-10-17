using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Creations;

public class JointFrameTests
{
    private const string Suspension = "test:suspension";
    private const string Heavy = "test:heavy";

    private static PartCatalog Catalog() => TestParts.Catalog()
        .Add(Suspension, new PartDefinition
        {
            Id = "suspension",
            Name = "Suspension",
            Category = PartCategory.Interactive,
            Mass = 5,
            Size = GridSize.One,
            Sockets =
            [
                new SocketDefinition { Id = "base", Type = SocketType.Structural, Position = new Vec3(-0.125f, 0, 0), Normal = new Vec3(-1, 0, 0) },
                new SocketDefinition
                {
                    Id = "mount", Type = SocketType.Spring, Position = new Vec3(0.125f, 0, 0), Normal = new Vec3(1, 0, 0),
                    Joint = new JointDefinition { Axis = new Vec3(0, 1, 0), Min = -0.1f, Max = 0.2f, Stiffness = 5000, Damping = 300 },
                },
            ],
        })
        .Add(Heavy, new PartDefinition
        {
            Id = "heavy",
            Name = "Heavy",
            Category = PartCategory.Block,
            Mass = 10,
            Size = GridSize.One,
            CentreOfMass = new Vec3(0, -0.1f, 0),
        });

    [Fact]
    public void SpringFrameUsesTheJointAxisTurnedWithThePart()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        // Turned a quarter about X: the part's +Y axis points along +Z.
        var turn = GridRotation.FromAxes(new GridCell(1, 0, 0), new GridCell(0, 0, 1));
        var suspension = creation.Place(Suspension, new GridCell(1, 0, 0), turn).Id;
        creation.Place(Block, new GridCell(2, 0, 0));

        var link = Assert.Single(creation.Joints);
        Assert.Equal(suspension, link.Connection.A);
        Assert.True(creation.TryGetJointFrame(link.Connection, out var frame));
        Assert.Equal(ConnectionType.Spring, frame.Type);
        Assert.Equal(new Vec3(0, 0, 1), frame.Axis);
        Assert.Equal(new Vec3(1, 0, 0), frame.Normal);
        Assert.Equal(new Vec3(0.375f, 0, 0), frame.Anchor);
        Assert.Equal((-0.1f, 0.2f, 5000f, 300f), (frame.Min, frame.Max, frame.Stiffness, frame.Damping));
        Assert.Equal(0.2f, frame.Clamp(1f));
    }

    [Fact]
    public void BearingFrameDefaultsToTheRotorNormalAndSpinsFreely()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        creation.Place(Bearing, new GridCell(0, 1, 0));
        creation.Place(Block, new GridCell(0, 2, 0));

        Assert.True(creation.TryGetJointFrame(creation.Joints[0].Connection, out var frame));
        Assert.Equal(new Vec3(0, 1, 0), frame.Axis);
        Assert.True(frame.SpinsFreely);
        Assert.Equal(5f, frame.Clamp(5f));
        Assert.False(creation.TryGetJointFrame(creation.Connections.First(c => c.Type == ConnectionType.Structural), out _));
    }

    [Fact]
    public void LinearFrameMinDefaultsToZero()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        creation.Place(Piston, new GridCell(0, 1, 0));
        creation.Place(Block, new GridCell(0, 2, 0));

        Assert.True(creation.TryGetJointFrame(creation.Joints[0].Connection, out var frame));
        Assert.Equal(0f, frame.Min);
    }

    [Fact]
    public void CentreOfMassOverrideMovesTheBodyCentre()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Heavy, GridCell.Zero);
        creation.Place(Block, new GridCell(1, 0, 0));

        var body = Assert.Single(creation.Bodies);
        // Heavy: 10 kg at (0, -0.1, 0); block: 4 kg at (0.25, 0, 0).
        Assert.Equal(1f / 14f, body.CentreOfMass.X, 4);
        Assert.Equal(-1f / 14f, body.CentreOfMass.Y, 4);
    }

    [Fact]
    public void CentreOfMassOverrideTurnsWithThePart()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Heavy, GridCell.Zero, GridRotation.FromAxes(new GridCell(0, 1, 0), new GridCell(-1, 0, 0)));

        Assert.Equal(new Vec3(0.1f, 0, 0), Assert.Single(creation.Bodies).CentreOfMass);
    }

    [Fact]
    public void PositionReadsTurnsAndSlides()
    {
        var hinge = new JointFrame(ConnectionType.Rotational, new Vec3(0, 0.125f, 0), new Vec3(0, 1, 0), new Vec3(0, 1, 0), null, null, null, null);
        var slider = new JointFrame(ConnectionType.Linear, new Vec3(0, 0.125f, 0), new Vec3(0, 1, 0), new Vec3(0, 1, 0), 0, 1, null, null);
        var a = new Pose(new Vector3(3, 1, 2), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f));

        Assert.Equal(0f, JointMath.Position(hinge, a, a), 5);
        var turned = a.Compose(new Pose(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f)));
        Assert.Equal(0.7f, JointMath.Position(hinge, a, turned), 4);
        var back = a.Compose(new Pose(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -3f)));
        Assert.Equal(-3f, JointMath.Position(hinge, a, back), 4);
        var wrapped = a.Compose(new Pose(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 4f)));
        Assert.Equal(4f - MathF.Tau, JointMath.Position(hinge, a, wrapped), 4);

        var slid = a.Compose(new Pose(new Vector3(0, 0.6f, 0), Quaternion.Identity));
        Assert.Equal(0.6f, JointMath.Position(slider, a, slid), 4);
        // Turning about the anchor does not slide it.
        var spun = a.Compose(new Pose(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f)));
        Assert.Equal(0f, JointMath.Position(slider, a, spun), 4);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(3.5f, 3.5f - MathF.Tau)]
    [InlineData(-3.5f, MathF.Tau - 3.5f)]
    [InlineData(MathF.PI, MathF.PI)]
    [InlineData(-MathF.PI, MathF.PI)]
    public void WrapKeepsAnglesWithinPi(float angle, float expected) => Assert.Equal(expected, JointMath.Wrap(angle), 4);
}
