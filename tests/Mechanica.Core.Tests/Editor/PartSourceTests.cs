using Mechanica.Core.Data;
using Mechanica.Core.Editor;

namespace Mechanica.Core.Tests.Editor;

public class PartSourceTests
{
    [Fact]
    public void WritesCanonicalJsonThatRoundTrips()
    {
        var source = PartSource.Empty with
        {
            Model =
            [
                ModelPiece.NewBox(new Vec3(0.25f, 0.25f, 0.25f)),
                new ModelPiece { Kind = ModelPieceKind.Cylinder, Operation = CsgOperation.Subtract, Radius = 0.05f, Height = 0.3f, Rotation = new Vec3(90, 0, 0) },
            ],
        };

        var json = PartSource.Write(source);

        Assert.Equal("""
            {
              "format_version": 1,
              "model": [
                {
                  "kind": "box",
                  "size": [0.25, 0.25, 0.25]
                },
                {
                  "kind": "cylinder",
                  "operation": "subtract",
                  "radius": 0.05,
                  "height": 0.3,
                  "rotation": [90, 0, 0]
                }
              ]
            }

            """.ReplaceLineEndings("\n"), json);
        Assert.Equal(json, PartSource.Write(PartSource.Read(json, "x", [])!));
    }

    [Theory]
    [InlineData("""{ "model": [{ }] }""", "$.model[0].kind")]
    [InlineData("""{ "model": [{ "kind": "box", "size": [0, 1, 1] }] }""", "$.model[0].size")]
    [InlineData("""{ "model": [{ "kind": "wedge" }] }""", "$.model[0].size")]
    [InlineData("""{ "model": [{ "kind": "sphere" }] }""", "$.model[0].radius")]
    [InlineData("""{ "model": [{ "kind": "mesh", "mesh": "../x.glb" }] }""", "$.model[0].mesh")]
    [InlineData("""{ "model": [{ "kind": "mesh" }] }""", "$.model[0].mesh")]
    [InlineData("""{ "model": [{ "kind": "sphere", "radius": 1, "scale": 0 }] }""", "$.model[0].scale")]
    [InlineData("""{ "model": [{ "kind": "sphere", "radius": 1, "operation": "subtract" }] }""", "$.model[0].operation")]
    [InlineData("""{ "format_version": 9 }""", "$.format_version")]
    public void InvalidPiecesAreReported(string json, string path)
    {
        var source = PartSource.Read(json, "x", [])!;

        var error = Assert.Single(PartSource.Validate(source, "m", "editor/x.json"));

        Assert.Equal(path, error.JsonPath);
        Assert.Equal(DiagnosticCodes.EditorSourceInvalid, error.Code);
    }

    [Fact]
    public void CylinderNeedsRadiusAndHeight() =>
        Assert.Equal(["$.model[0].radius", "$.model[0].height"],
            PartSource.Validate(PartSource.Read("""{ "model": [{ "kind": "cylinder" }] }""", "x", [])!, "m", "x").Select(d => d.JsonPath));

    [Fact]
    public void OnlyPrimitivesOrTransformedMeshesNeedBaking()
    {
        Assert.False(PartSource.Empty.NeedsBake);
        Assert.False((PartSource.Empty with { Model = [ModelPiece.NewMesh("meshes/a.glb")] }).NeedsBake);
        Assert.True((PartSource.Empty with { Model = [ModelPiece.NewMesh("meshes/a.glb") with { Scale = 2 }] }).NeedsBake);
        Assert.True((PartSource.Empty with { Model = [ModelPiece.NewBox(new Vec3(1, 1, 1))] }).NeedsBake);
        Assert.True((PartSource.Empty with { Model = [ModelPiece.NewMesh("meshes/a.glb"), ModelPiece.NewMesh("meshes/b.glb")] }).NeedsBake);
    }
}
