using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Creations;

/// <summary>The graph operations behind the Weld, Paint and Sledgehammer tools.</summary>
public class CreationToolTests
{
    private static readonly GridCell Up = new(0, 1, 0);

    private readonly Creation _creation = new(1, Catalog());

    [Fact]
    public void WeldingPartsAcrossABearingMergesTheBodies()
    {
        _creation.Place(Block, GridCell.Zero);
        _creation.Place(Bearing, Up);
        _creation.Place(Block, Up + Up);
        Assert.Equal(2, _creation.Bodies.Count);

        Assert.True(_creation.TryWeldParts(3, 2, out var error), error);

        Assert.Single(_creation.Bodies);
        Assert.True(Assert.Single(_creation.Joints).IsLocked);
        Assert.Contains(Connection.Rigid(2, 3), _creation.Connections);
    }

    [Fact]
    public void WeldingPartsIsChecked()
    {
        _creation.Place(Block, GridCell.Zero);
        _creation.Place(Bearing, Up);
        _creation.Place(Block, Up + Up);
        _creation.Place(Block, new GridCell(1, 0, 0));

        Assert.False(_creation.CanWeldParts(1, 4, out var error));
        Assert.Contains("already one rigid body", error);
        Assert.False(_creation.CanWeldParts(3, 4, out error));
        Assert.Contains("do not touch", error);
        Assert.False(_creation.CanWeldParts(3, 3, out error));
        Assert.Contains("itself", error);
        Assert.False(_creation.CanWeldParts(3, 9, out error));
        Assert.Contains("no part 9", error);
        Assert.Equal(2, _creation.Bodies.Count);
    }

    [Fact]
    public void MergeDryRunReportsTheAnchorsAndChangesNothing()
    {
        _creation.Place(Block, GridCell.Zero);
        _creation.Place(Block, Up);
        var other = new Creation(2, _creation.Catalog);
        other.Place(Capped, GridCell.Zero);
        other.Place(Block, Up);

        Assert.True(_creation.CanMergeFrom(other, default, new GridCell(1, 0, 0), weld: true, out var attachedTo, out var error), error);

        Assert.Equal([1, 2], attachedTo);
        Assert.Equal(2, _creation.Parts.Count);
        Assert.Single(_creation.Bodies);
        Assert.Equal(3, _creation.Place(Block, new GridCell(-1, 0, 0)).Id);
        Assert.False(_creation.CanMergeFrom(other, default, new GridCell(-1, 0, 0), weld: true, out _, out error));
        Assert.Contains("overlap", error);
    }

    [Fact]
    public void PaintIsStoredOnThePartAndSurvivesSplitsAndMerges()
    {
        _creation.Place(Block, GridCell.Zero);
        _creation.Place(Block, Up);
        _creation.Place(Block, Up + Up);

        Assert.True(_creation.SetPartColor(3, "#D03030"));
        Assert.False(_creation.SetPartColor(3, "#d03030"));
        Assert.Equal("#d03030", _creation.FindPart(3)!.Color);
        Assert.Throws<ArgumentException>(() => _creation.SetPartColor(3, "red"));

        var split = Assert.Single(_creation.RemovePart(2, Ids()).SplitOff);
        Assert.Equal("#d03030", split.FindPart(3)!.Color);

        Assert.True(_creation.TryMergeFrom(split, default, new GridCell(0, -1, 0), weld: true, out var map, out var error), error);
        Assert.Equal("#d03030", _creation.FindPart(map[3])!.Color);
        Assert.True(_creation.SetPartColor(map[3], null));
        Assert.Null(_creation.FindPart(map[3])!.Color);
    }

    [Fact]
    public void DamageFollowsThePart()
    {
        _creation.Place(Block, GridCell.Zero);
        _creation.Place(Block, Up);
        _creation.Place(Block, Up + Up);
        _creation.SetPartDamage(3, 12.5f);

        var split = Assert.Single(_creation.RemovePart(2, Ids()).SplitOff);
        Assert.Equal(12.5f, split.PartDamage(3));
        Assert.Equal(0f, _creation.PartDamage(1));

        split.SetPartDamage(3, 0);
        Assert.Equal(0f, split.PartDamage(3));
    }

    [Fact]
    public void PaintDamageAndLiftRoundTripThroughJson()
    {
        _creation.Place(Block, GridCell.Zero);
        _creation.Place(Block, Up);
        _creation.SetPartColor(2, "#3070d0");
        _creation.SetPartDamage(1, 7.5f);
        _creation.SetAnchored(1, true);
        _creation.OnLift = true;
        _creation.SetAllBodyPoses(new Pose(new Vector3(1, 2, 3), Quaternion.Identity));

        var json = CreationJson.Write(_creation);
        var diagnostics = new List<DataDiagnostic>();
        var loaded = CreationJson.Read(json, _creation.Catalog, "painted.json", diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(loaded);
        Assert.Contains("\"on_lift\": true", json);
        Assert.Contains("\"color\": \"#3070d0\"", json);
        Assert.True(loaded.OnLift);
        Assert.Equal(_creation.Parts, loaded.Parts);
        Assert.Equal(7.5f, loaded.PartDamage(1));
        Assert.Equal([1], loaded.AnchoredParts);
        Assert.Equal(json, CreationJson.Write(loaded));
    }

    [Fact]
    public void UnpaintedUndamagedCreationsWriteNoToolKeys()
    {
        _creation.Place(Block, GridCell.Zero);
        var json = CreationJson.Write(_creation);
        Assert.DoesNotContain("color", json);
        Assert.DoesNotContain("damage", json);
        Assert.DoesNotContain("on_lift", json);
        Assert.DoesNotContain("anchored", json);
    }

    [Theory]
    [InlineData("\"color\": \"blue\"", "$.parts[0].color")]
    [InlineData("\"damage\": -1", "$.parts[0].damage")]
    public void BadToolKeysAreErrors(string field, string path)
    {
        var json = $$"""{"format_version": 1, "id": 1, "parts": [{"id": 1, "part": "test:block", "cell": [0, 0, 0], {{field}}}]}""";
        var diagnostics = new List<DataDiagnostic>();

        Assert.Null(CreationJson.Read(json, Catalog(), "bad.json", diagnostics));
        Assert.Contains(diagnostics, d => d.JsonPath == path);
    }
}
