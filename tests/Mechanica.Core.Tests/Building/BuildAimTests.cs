using System.Numerics;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Building;

public class BuildAimTests
{
    private static readonly GridCell Up = new(0, 1, 0);
    private static readonly GridCell East = new(1, 0, 0);
    private static readonly GridCell South = new(0, 0, 1);

    private static readonly PartDefinition Cube = new() { Category = PartCategory.Block, Size = GridSize.One };
    private static readonly PartDefinition Plank = new() { Category = PartCategory.Block, Size = new GridSize(3, 1, 2) };

    [Fact]
    public void HitOnATopFaceTargetsTheCellAbove()
    {
        // Top face of cell [1, 0, 0] is at y = 0.125.
        var (cell, face) = BuildAim.FromHit(new Vector3(0.3f, 0.125f, 0.05f), new Vector3(0.02f, 0.99f, 0));
        Assert.Equal(East, cell);
        Assert.Equal(Up, face);
        Assert.Equal(new GridCell(1, 1, 0), cell + face);
    }

    [Fact]
    public void HitOnASideFaceTargetsTheNeighbour()
    {
        var (cell, face) = BuildAim.FromHit(new Vector3(-0.125f, 0.1f, 0), new Vector3(-1, 0, 0));
        Assert.Equal(GridCell.Zero, cell);
        Assert.Equal(new GridCell(-1, 0, 0), face);
    }

    [Fact]
    public void OneCellPartsAnchorOnTheTarget()
    {
        foreach (var face in GridCell.Directions)
            Assert.Equal(new GridCell(4, 5, 6), BuildAim.Anchor(Cube, GridRotation.Identity, new GridCell(4, 5, 6), face));
    }

    [Theory]
    [MemberData(nameof(FacesAndRotations))]
    public void LargePartsSitOnTheFaceSideAndCoverTheTarget(GridCell face, GridRotation rotation)
    {
        var target = new GridCell(2, 2, 2);
        var origin = BuildAim.Anchor(Plank, rotation, target, face);
        var cells = PartGeometry.Cells(Plank, origin, rotation).ToList();

        Assert.Contains(target, cells);
        var layer = cells.Select(c => Dot(c - target, face)).ToList();
        Assert.Equal(0, layer.Min());
    }

    public static TheoryData<GridCell, GridRotation> FacesAndRotations()
    {
        var data = new TheoryData<GridCell, GridRotation>();
        foreach (var face in GridCell.Directions)
            foreach (var rotation in GridRotation.All)
                data.Add(face, rotation);
        return data;
    }

    [Fact]
    public void ContactLayerIsCentredOnTheTarget()
    {
        // A 3-long plank laid on a top face: the middle cell lands on the target.
        var origin = BuildAim.Anchor(Plank, GridRotation.Identity, GridCell.Zero, Up);
        Assert.Equal(new GridCell(-1, 0, 0), origin);
    }

    [Fact]
    public void PartsStandOnTheFaceAndLookAtTheBuilder()
    {
        var onTop = BuildAim.Orient(Up, new Vector3(0, 0.5f, 1), GridRotation.Identity);
        Assert.Equal(GridRotation.Identity, onTop);

        var onEastWall = BuildAim.Orient(East, new Vector3(1, 0, -0.2f), GridRotation.Identity);
        Assert.Equal(East, onEastWall.YAxis);
        Assert.Equal(new GridCell(0, 0, -1), onEastWall.ZAxis);
    }

    [Fact]
    public void LookingStraightAlongTheNormalStillGivesARotation()
    {
        var rotation = BuildAim.Orient(Up, new Vector3(0, 1, 0), GridRotation.Identity);
        Assert.Equal(Up, rotation.YAxis);
    }

    [Fact]
    public void TurningSpinsAboutTheFaceNormal()
    {
        var adjust = GridRotation.Identity;
        var seen = new HashSet<GridRotation>();
        for (var i = 0; i < 4; i++)
        {
            var rotation = BuildAim.Orient(East, Vector3.UnitX, adjust);
            Assert.Equal(East, rotation.YAxis);
            seen.Add(rotation);
            adjust = BuildAim.Turn(adjust);
        }
        Assert.Equal(4, seen.Count);
        Assert.Equal(GridRotation.Identity, adjust);
    }

    [Fact]
    public void TurnAndTipReachAll24Orientations()
    {
        // Breadth-first over Turn and Tip from the identity, as a player pressing the two keys would.
        var seen = new HashSet<GridRotation> { GridRotation.Identity };
        var queue = new Queue<GridRotation>([GridRotation.Identity]);
        while (queue.TryDequeue(out var adjust))
        {
            foreach (var next in new[] { BuildAim.Turn(adjust), BuildAim.Tip(adjust) })
                if (seen.Add(next))
                    queue.Enqueue(next);
        }
        Assert.Equal(24, seen.Count);
        Assert.Equal(24, seen.Select(a => BuildAim.Orient(South, Vector3.UnitZ, a)).Distinct().Count());
    }

    [Fact]
    public void TipPutsTheWedgeOnItsSide()
    {
        var tipped = BuildAim.Orient(Up, Vector3.UnitZ, BuildAim.Tip(GridRotation.Identity));
        Assert.Equal(South, tipped.YAxis);
        Assert.Equal(-Up, tipped.ZAxis);
    }

    [Fact]
    public void SnapRoundsToTheGrid()
    {
        Assert.Equal(new Vector3(1.25f, 0f, -0.5f), BuildAim.Snap(new Vector3(1.3f, 0.1f, -0.45f)));
    }

    private static int Dot(GridCell a, GridCell b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
