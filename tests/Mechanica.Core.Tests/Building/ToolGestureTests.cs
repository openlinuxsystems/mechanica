using System.Numerics;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Building;

public class ToolGestureTests
{
    private static readonly GridCell A = GridCell.Zero;
    private static readonly GridCell B = new(1, 0, 0);

    [Fact]
    public void AQuickReleaseIsAClick()
    {
        var gesture = new ToolGesture(0.15, 0.1);
        gesture.Press(10, A);
        Assert.Equal(GesturePhase.Pressed, gesture.Update(10.1, A));
        Assert.Equal(GestureEnd.Click, gesture.Release(10.12));
        Assert.False(gesture.Down);
        Assert.Null(gesture.Release(10.2));
    }

    [Fact]
    public void HoldingStillAligns()
    {
        var gesture = new ToolGesture(0.15, 0.1);
        gesture.Press(0, A);
        Assert.Equal(GesturePhase.Pressed, gesture.Update(0.14, A));
        Assert.Equal(GesturePhase.Holding, gesture.Update(0.15, A));
        Assert.Equal(GestureEnd.Hold, gesture.Release(0.5));
    }

    [Fact]
    public void MovingForTheDragTimeDrags()
    {
        var gesture = new ToolGesture(0.15, 0.1);
        gesture.Press(0, A);
        Assert.Equal(GesturePhase.Pressed, gesture.Update(0.05, B));
        Assert.Equal(GesturePhase.Pressed, gesture.Update(0.14, B));
        Assert.Equal(GesturePhase.Dragging, gesture.Update(0.16, B));
        // Once dragging, coming back to the start cell still drags.
        Assert.Equal(GesturePhase.Dragging, gesture.Update(0.3, A));
        Assert.Equal((A, A), (gesture.Start, gesture.Current));
        Assert.Equal(GestureEnd.Drag, gesture.Release(0.4));
    }

    [Fact]
    public void AFlickBackToTheStartIsNoDrag()
    {
        var gesture = new ToolGesture(0.15, 0.1);
        gesture.Press(0, A);
        gesture.Update(0.02, B);
        gesture.Update(0.05, A);
        Assert.Equal(GesturePhase.Pressed, gesture.Update(0.11, A));
        Assert.Equal(GesturePhase.Holding, gesture.Update(0.2, A));
        gesture.Update(0.25, B);
        Assert.Equal(GesturePhase.Dragging, gesture.Update(0.36, B));
    }

    [Fact]
    public void ACancelEndsWithoutAnEnd()
    {
        var gesture = new ToolGesture(0.15, 0.1);
        gesture.Press(0, A);
        gesture.Cancel();
        Assert.Null(gesture.Release(1));
    }

    [Fact]
    public void ADragRectangleStepsByThePartAndStopsAtTheLimit()
    {
        var row = BuildDrag.Cells(A, new GridCell(5, 3, 0), 1, new GridSize(1, 1, 1), 16);
        Assert.Equal([A, new(1, 0, 0), new(2, 0, 0), new(3, 0, 0), new(4, 0, 0), new(5, 0, 0)], row);

        var area = BuildDrag.Cells(A, new GridCell(-3, 0, 2), 1, new GridSize(2, 1, 1), 16);
        Assert.Equal([A, new(-2, 0, 0), new(0, 0, 1), new(-2, 0, 1), new(0, 0, 2), new(-2, 0, 2)], area.OrderBy(c => c.Z).ThenByDescending(c => c.X));
        Assert.Equal(A, area[0]);

        var big = BuildDrag.Cells(A, new GridCell(100, 0, -100), 1, new GridSize(1, 1, 1), 16);
        Assert.Equal(256, big.Count);
        Assert.Equal(15, big.Max(c => c.X));
        Assert.Equal(-15, big.Min(c => c.Z));
    }

    [Fact]
    public void EachCellAfterTheFirstTouchesOneBefore()
    {
        var cells = BuildDrag.Cells(A, new GridCell(4, 0, 4), 1, new GridSize(1, 1, 1), 16);
        for (var i = 1; i < cells.Count; i++)
        {
            var cell = cells[i];
            Assert.Contains(cells.Take(i), c => Math.Abs(c.X - cell.X) + Math.Abs(c.Y - cell.Y) + Math.Abs(c.Z - cell.Z) == 1);
        }
    }

    [Fact]
    public void TheAimMeetsThePlaneInACell()
    {
        // From above, looking down and along +x, onto the floor plane through cell 0.
        Assert.Equal(new GridCell(4, 0, 0), BuildDrag.OnPlane(new Vector3(0.5f, 4.5f, 0.5f), Vector3.Normalize(new Vector3(1, -1, 0)), A, 1));
        Assert.Null(BuildDrag.OnPlane(new Vector3(0.5f, 4.5f, 0.5f), new Vector3(1, 0, 0), A, 1));
        Assert.Null(BuildDrag.OnPlane(new Vector3(0.5f, 4.5f, 0.5f), new Vector3(0, 1, 0), A, 1));
    }

    [Fact]
    public void TheAimMeetsTheFaceItStartedOn()
    {
        // Looking down at the top of cell 0 at a low angle: the hit is on its top face, in cell 0.
        var origin = new Vector3(0.5f, 1.5f, 3.5f);
        var direction = Vector3.Normalize(new Vector3(0.5f, 1f, 0.5f) - origin);
        Assert.Equal(A, BuildDrag.OnFace(origin, direction, A, new GridCell(0, 1, 0)));
        // The same ray against the plane through the cell's centre lands a cell nearer.
        Assert.NotEqual(A, BuildDrag.OnPlane(origin, direction, A, 1));
        Assert.Null(BuildDrag.OnFace(origin, -direction, A, new GridCell(0, 1, 0)));
    }

    [Fact]
    public void QStepsThroughThePlanes()
    {
        Assert.Equal([1, 2, 0], new[] { BuildDrag.NextPlane(0), BuildDrag.NextPlane(1), BuildDrag.NextPlane(2) });
        Assert.Equal(1, BuildDrag.AxisOf(new GridCell(0, -1, 0)));
        Assert.Equal("XZ", BuildDrag.PlaneName(1));
        Assert.All(BuildDrag.Directions(2), d => Assert.Equal(0, d.Z));
    }

    [Fact]
    public void RemovalTakesTheMaterialsTime()
    {
        var settings = BuildSettings.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "data", "building.json"))).Placement;
        Assert.Equal(0.4, settings.RemoveSecondsFor("wood"));
        Assert.Equal(0.7, settings.RemoveSecondsFor("metal"));
        Assert.Equal(0.9, settings.RemoveSecondsFor("concrete"));
        Assert.Equal(0.6, settings.RemoveSecondsFor("plastic"));
        Assert.Equal(0.6, settings.RemoveSecondsFor(null));
        Assert.Equal((0.15, 0.1, 256), (settings.AlignHoldSeconds, settings.DragSeconds, settings.MaxPartsPerAction));
    }

    [Fact]
    public void AStepFollowsThePartsTurn()
    {
        var plank = new PartDefinition { Size = new GridSize(3, 1, 1) };
        Assert.Equal(new GridSize(3, 1, 1), BuildDrag.StepOf(plank, GridRotation.Identity));
        Assert.Equal(new GridSize(1, 1, 3), BuildDrag.StepOf(plank, GridRotation.FromAxes(new GridCell(0, 0, 1), new GridCell(0, 1, 0))));
    }
}
