using System.Text.Json.Nodes;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Creations;

public class CreationJsonTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "creations", "bearing_stack.json");

    private static readonly ModLoadResult SampleMods = ModLoader.LoadAll(TestMods.FixtureRoot);

    [Fact]
    public void FixtureLoadsAgainstTheSampleModAndIsCanonical()
    {
        var text = File.ReadAllText(FixturePath);
        var diagnostics = new List<DataDiagnostic>();

        var creation = CreationJson.Read(text, SampleMods, "bearing_stack.json", diagnostics);

        Assert.NotNull(creation);
        Assert.Equal(7, creation.Id);
        Assert.Equal(2, creation.Bodies.Count);
        Assert.Equal(ConnectionType.Rotational, Assert.Single(creation.Joints).Connection.Type);
        // The unknown "paint" key is kept with a warning and written back.
        Assert.Equal(DiagnosticCodes.UnknownKey, Assert.Single(diagnostics).Code);
        Assert.Equal(text, CreationJson.Write(creation));
    }

    [Fact]
    public void SampleWoodBlocksAttachSideBySide()
    {
        var creation = new Creation(1, SampleMods);
        creation.Place("sample:wood_block", GridCell.Zero);
        creation.Place("sample:wood_block", new GridCell(1, 0, 0));
        Assert.Equal([Connection.Rigid(1, 2)], creation.Connections);
    }

    [Fact]
    public void BuiltGraphRoundTrips()
    {
        var creation = new Creation(12, Catalog(), authorityPeer: 2);
        creation.Place(Block, GridCell.Zero);
        creation.Place(Bearing, new GridCell(0, 1, 0));
        creation.Place(Beam, new GridCell(0, 2, 0), GridRotation.FromAxes(new GridCell(0, 0, -1), new GridCell(0, 1, 0)));
        creation.Place(Piston, new GridCell(1, 0, 0), GridRotation.FromAxes(new GridCell(0, 1, 0), new GridCell(-1, 0, 0)));
        creation.Place(Block, new GridCell(2, 0, 0));

        var json = CreationJson.Write(creation);
        var diagnostics = new List<DataDiagnostic>();
        var loaded = CreationJson.Read(json, creation.Catalog, "built.json", diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(loaded);
        Assert.Equal(json, CreationJson.Write(loaded));
        Assert.Equal(creation.Parts, loaded.Parts);
        Assert.Equal(creation.Connections, loaded.Connections);
        Assert.Equal(creation.Bodies.Select(b => b.PartIds), loaded.Bodies.Select(b => b.PartIds));
        Assert.Equal(2, loaded.AuthorityPeer);
        // New parts continue after the highest loaded id.
        Assert.Equal(6, loaded.Place(Block, new GridCell(-1, 0, 0)).Id);
    }

    [Fact]
    public void LenientInputIsAccepted()
    {
        const string json = """
            {
              // a hand-edited blueprint
              "format_version": 1, "id": 3,
              "parts": [ { "id": 5, "part": "test:block", "cell": [0, 0, 0], }, ],
            }
            """;
        var creation = CreationJson.Read(json, Catalog(), "x.json", []);
        Assert.NotNull(creation);
        Assert.Equal(Creation.HostPeer, creation.AuthorityPeer);
        Assert.Equal(GridRotation.Identity, creation.FindPart(5)!.Rotation);
    }

    [Fact]
    public void AnUpgradedPartsSettingsAreCheckedAgainstItsLevel()
    {
        // A Sensor 5 reaches 5 m, a Sensor 1 only 1 m: a saved range of 4.5 m is checked against the saved level.
        var vanilla = ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"));
        var creation = new Creation(4, vanilla);
        creation.Place("vanilla:metal_block", GridCell.Zero);
        var sensor = creation.Place("vanilla:sensor", new GridCell(0, 1, 0)).Id;
        Assert.True(creation.SetLevel(sensor, 4));
        var changes = new Dictionary<string, System.Text.Json.JsonElement?> { ["range"] = System.Text.Json.JsonSerializer.SerializeToElement(4.5) };
        creation.SetPartSettings(sensor, Core.Logic.PartSettings.Apply(creation.DefinitionOf(sensor), creation.FindPart(sensor)!.Settings, changes));
        var json = CreationJson.Write(creation);

        var diagnostics = new List<DataDiagnostic>();
        var loaded = CreationJson.Read(json, vanilla, "sensor.json", diagnostics);
        Assert.Empty(diagnostics);
        Assert.Equal(4.5, loaded!.FindPart(sensor)!.Settings!["range"].GetDouble());

        var level1 = JsonNode.Parse(json)!;
        level1["parts"]![1]!.AsObject().Remove("level");
        Assert.Null(CreationJson.Read(level1.ToJsonString(), vanilla, "sensor.json", diagnostics));
        Assert.Contains(diagnostics, d => d.JsonPath == "$.parts[1].settings");
    }

    public static TheoryData<string, string, string> BadSaves() => new()
    {
        { """{ "id": 1, "parts": [ { "id": 1, "part": "test:block", "cell": [0, 0, 0] } ] }""", DiagnosticCodes.CreationInvalid, "format_version is required" },
        { """{ "format_version": 2, "id": 1, "parts": [ { "id": 1, "part": "test:block", "cell": [0, 0, 0] } ] }""", DiagnosticCodes.UnsupportedFormat, "not supported" },
        { """{ "format_version": 1, "id": 1, "parts": [] }""", DiagnosticCodes.CreationInvalid, "at least one part" },
        { """{ "format_version": 1, "id": 1, "authority_peer": 0, "parts": [ { "id": 1, "part": "test:block", "cell": [0, 0, 0] } ] }""",
            DiagnosticCodes.CreationInvalid, "authority_peer" },
        { Save("""{ "id": 1, "part": "test:nope", "cell": [0, 0, 0] }"""), DiagnosticCodes.CreationUnknownPart, "unknown part 'test:nope'" },
        { Save("""{ "id": 1, "part": "test:ore", "cell": [0, 0, 0] }"""), DiagnosticCodes.CreationInvalid, "cannot be placed" },
        { Save("""{ "id": 0, "part": "test:block", "cell": [0, 0, 0] }"""), DiagnosticCodes.CreationInvalid, "1 or more" },
        { Save("""{ "id": 1, "part": "test:block" }"""), DiagnosticCodes.CreationInvalid, "cell is required" },
        { Save("""{ "id": 1, "part": "test:block", "cell": [0, 0, 0] }, { "id": 1, "part": "test:block", "cell": [1, 0, 0] }"""),
            DiagnosticCodes.CreationInvalid, "used twice" },
        { Save("""{ "id": 1, "part": "test:beam", "cell": [0, 0, 0] }, { "id": 2, "part": "test:block", "cell": [1, 0, 0] }"""),
            DiagnosticCodes.CreationOverlap, "overlaps part 1 at [1, 0, 0]" },
        { Save("""{ "id": 1, "part": "test:block", "cell": [0, 0, 0] }, { "id": 2, "part": "test:block", "cell": [1, 0, 0] }"""),
            DiagnosticCodes.CreationDisconnected, "2 separate pieces" },
        { Save("""{ "id": 1, "part": "test:block", "cell": [0, 0, 0] }, { "id": 2, "part": "test:block", "cell": [3, 0, 0] }""",
                """{ "type": "structural", "a": 1, "b": 2 }"""),
            DiagnosticCodes.CreationConnection, "do not touch" },
        { Save("""{ "id": 1, "part": "test:block", "cell": [0, 0, 0] }, { "id": 2, "part": "test:bearing", "cell": [0, 1, 0] }""",
                """{ "type": "linear", "a": 2, "a_socket": "rotor", "b": 1 }"""),
            DiagnosticCodes.CreationConnection, "is rotational, not linear" },
        { Save("""{ "id": 1, "part": "test:block", "cell": [0, 0, 0] }, { "id": 2, "part": "test:block", "cell": [1, 0, 0] }""",
                """{ "type": "structural", "a": 1, "b": 2 }, { "type": "structural", "a": 1, "b": 2 }"""),
            DiagnosticCodes.CreationConnection, "listed twice" },
        { Save("""{ "id": 1, "part": "test:block", "cell": [0, 0, 0], "rotation": "+x+x" }"""), DiagnosticCodes.InvalidJson, "grid rotation" },
        { Save("""{ "id": 1, "part": "test:block", "cell": [0, 0.5, 0] }"""), DiagnosticCodes.InvalidJson, "whole grid cells" },
    };

    [Theory]
    [MemberData(nameof(BadSaves))]
    public void BadSavesGiveClearErrors(string json, string code, string message)
    {
        var diagnostics = new List<DataDiagnostic>();
        Assert.Null(CreationJson.Read(json, Catalog(), "bad.json", diagnostics));
        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(code, error.Code);
        Assert.Contains(message, error.Message);
        Assert.StartsWith("error MD", error.ToString());
    }

    [Fact]
    public void UnknownConnectionKeysWarnAndAreDropped()
    {
        var json = Save("""{ "id": 1, "part": "test:block", "cell": [0, 0, 0] }, { "id": 2, "part": "test:block", "cell": [1, 0, 0] }""",
            """{ "type": "structural", "a": 1, "b": 2, "strength": 3 }""");
        var diagnostics = new List<DataDiagnostic>();
        var creation = CreationJson.Read(json, Catalog(), "x.json", diagnostics);
        Assert.NotNull(creation);
        Assert.Equal("$.connections[0].strength", Assert.Single(diagnostics).JsonPath);
        Assert.Null(JsonNode.Parse(CreationJson.Write(creation))!["connections"]![0]!["strength"]);
    }

    private static string Save(string parts, string connections = "") =>
        $$"""{ "format_version": 1, "id": 1, "parts": [ {{parts}} ], "connections": [ {{connections}} ] }""";
}
