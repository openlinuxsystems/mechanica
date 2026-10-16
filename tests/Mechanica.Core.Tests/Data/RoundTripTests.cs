using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Data;

public class RoundTripTests
{
    public static TheoryData<string> FixtureFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(TestMods.FixtureRoot, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            data.Add(Path.GetRelativePath(TestMods.FixtureRoot, file).Replace('\\', '/'));
        return data;
    }

    /// <summary>Fixture files are kept in canonical form, so writing what was read reproduces them byte for byte.</summary>
    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void FixtureFilesAreCanonical(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(TestMods.FixtureRoot, relativePath));

        var written = Path.GetFileName(relativePath) == ModLoader.ManifestFileName
            ? DataJson.Write(DataJson.Read<ModManifest>(text)!)
            : DataJson.Write(DataJson.Read<PartDefinition>(text)!);

        Assert.Equal(text, written);
    }

    [Fact]
    public void VanillaManifestIsCanonical()
    {
        var text = File.ReadAllText(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", "mod.json"));
        Assert.Equal(text, DataJson.Write(DataJson.Read<ModManifest>(text)!));
    }

    [Fact]
    public void LenientInputBecomesCanonicalAndStaysStable()
    {
        const string messy = """
            // hand-written
            {
              "zeta_unknown": { "b": 1, "a": [1, 2] },
              "name": "Wing", "id": "wing", "category": "interactive",
              "size": [4,1,2], "mass": 2, "durability": 2, "friction": 0.3, "buoyancy": 0.2, "flammable": false,
              "properties": { "lift_coefficient": 1.2, "area": 2, "curve": [0, 0.5, 1.0e0] },
              "sockets": [ { "normal": [0,-1,0], "position": [0,-0.125,0], "type": "structural", "id": "mount", "x_note": true, }, ],
              "alpha_unknown": "kept",
            }
            """;

        var first = DataJson.Write(DataJson.Read<PartDefinition>(messy)!);
        var second = DataJson.Write(DataJson.Read<PartDefinition>(first)!);

        Assert.Equal(first, second);
        Assert.Equal("""
            {
              "id": "wing",
              "name": "Wing",
              "category": "interactive",
              "mass": 2,
              "durability": 2,
              "friction": 0.3,
              "buoyancy": 0.2,
              "flammable": false,
              "size": [4, 1, 2],
              "sockets": [
                {
                  "id": "mount",
                  "type": "structural",
                  "position": [0, -0.125, 0],
                  "normal": [0, -1, 0],
                  "x_note": true
                }
              ],
              "properties": {
                "lift_coefficient": 1.2,
                "area": 2,
                "curve": [
                  0,
                  0.5,
                  1.0e0
                ]
              },
              "alpha_unknown": "kept",
              "zeta_unknown": {
                "b": 1,
                "a": [
                  1,
                  2
                ]
              }
            }

            """.ReplaceLineEndings("\n"), first);
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.3f)]
    [InlineData(-0.125f)]
    [InlineData(1e-7f)]
    [InlineData(12345.678f)]
    public void FloatsRoundTripExactly(float value)
    {
        var part = new PartDefinition { Id = "p", Mass = value, Sockets = [new SocketDefinition { Position = new Vec3(value, 0, value) }] };

        var back = DataJson.Read<PartDefinition>(DataJson.Write(part))!;

        Assert.Equal(value, back.Mass);
        Assert.Equal(new Vec3(value, 0, value), back.Sockets![0].Position);
    }

    [Fact]
    public void WriterOutputIsLfWithTrailingNewlineAndNoBom()
    {
        var text = DataJson.Write(new PartDefinition { Id = "p", Name = "Ünïcode \"quoted\"" });

        Assert.DoesNotContain("\r", text);
        Assert.EndsWith("}\n", text);
        Assert.Contains("\"name\": \"Ünïcode \\\"quoted\\\"\"", text);

        var path = Path.Combine(Path.GetTempPath(), $"mechanica-{Guid.NewGuid():N}.json");
        try
        {
            DataJson.WriteFile(path, text);
            Assert.Equal((byte)'{', File.ReadAllBytes(path)[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
