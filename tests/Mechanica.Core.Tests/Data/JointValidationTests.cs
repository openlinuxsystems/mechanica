using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Data;

public class JointValidationTests
{
    private static IReadOnlyList<DataDiagnostic> Check(string socketJson, string partOverrides = "")
    {
        using var mods = new TestMods().Mod("m");
        mods.Part("m", "p.json", TestMods.Block(overrides: $$"""
            {
              "category": "interactive"{{partOverrides}},
              "sockets": [
                { "id": "base", "type": "structural", "position": [0, -0.125, 0], "normal": [0, -1, 0] },
                {{socketJson}}
              ]
            }
            """));
        return [.. mods.Load().Diagnostics.Where(d => d.File == "mods/m/parts/p.json")];
    }

    [Theory]
    [InlineData("""{ "id": "rotor", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0] }""")]
    [InlineData("""{ "id": "rotor", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "min": -0.5, "max": 0.5 } }""")]
    [InlineData("""{ "id": "rotor", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "inset": 0.17 } }""")]
    [InlineData("""{ "id": "head", "type": "linear", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "max": 1 } }""")]
    [InlineData("""{ "id": "mount", "type": "spring", "position": [0.125, 0, 0], "normal": [1, 0, 0], "joint": { "axis": [0, 1, 0], "min": -0.1, "max": 0.1, "stiffness": 8000, "damping": 500 } }""")]
    [InlineData("""{ "id": "turret", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "pitch_axis": [1, 0, 0], "pitch_min": -0.3, "pitch_max": 0.8 } }""")]
    [InlineData("""{ "id": "turret", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "min": -1, "max": 1, "pivot": [0, -0.05, 0], "pitch_axis": [0, 0, 1], "pitch_min": -1.5, "pitch_max": 1.5 } }""")]
    public void ValidJointsPass(string socket) => Assert.Empty(Check(socket));

    [Theory]
    [InlineData("""{ "id": "s", "type": "structural", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": {} }""", "$.sockets[1].joint", "only rotational, linear, spring and aimed")]
    [InlineData("""{ "id": "s", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "axis": [0, 0.7, 0.7] } }""", "$.sockets[1].joint.axis", "grid direction")]
    [InlineData("""{ "id": "s", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "min": -1 } }""", "$.sockets[1].joint.max", "both min and max")]
    [InlineData("""{ "id": "s", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "min": -4, "max": 1 } }""", "$.sockets[1].joint.min", "within ±π")]
    [InlineData("""{ "id": "s", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "min": 0.2, "max": 1 } }""", "$.sockets[1].joint.min", "include 0")]
    [InlineData("""{ "id": "s", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "stiffness": 5 } }""", "$.sockets[1].joint.stiffness", "only spring joints")]
    [InlineData("""{ "id": "s", "type": "linear", "position": [0, 0.125, 0], "normal": [0, 1, 0] }""", "$.sockets[1].joint", "needs a joint with max")]
    [InlineData("""{ "id": "s", "type": "linear", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": {} }""", "$.sockets[1].joint.max", "required for a linear joint")]
    [InlineData("""{ "id": "s", "type": "linear", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "max": 20 } }""", "$.sockets[1].joint.max", "most allowed is 16")]
    [InlineData("""{ "id": "s", "type": "spring", "position": [0, 0.125, 0], "normal": [0, 1, 0] }""", "$.sockets[1].joint", "needs a joint with min, max and stiffness")]
    [InlineData("""{ "id": "s", "type": "spring", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "min": -0.1, "max": 0.1 } }""", "$.sockets[1].joint.stiffness", "required")]
    [InlineData("""{ "id": "s", "type": "spring", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "min": -0.1, "max": 0.1, "stiffness": 0 } }""", "$.sockets[1].joint.stiffness", "more than 0")]
    [InlineData("""{ "id": "s", "type": "spring", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "min": -0.1, "max": 0.1, "stiffness": 10, "damping": -1 } }""", "$.sockets[1].joint.damping", "0 or more")]
    [InlineData("""{ "id": "s", "type": "spring", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "min": 0.1, "max": 0.2, "stiffness": 10 } }""", "$.sockets[1].joint.min", "include 0")]
    [InlineData("""{ "id": "s", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0] }""", "$.sockets[1].joint", "needs a joint with pitch_axis")]
    [InlineData("""{ "id": "s", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "pitch_min": -0.3, "pitch_max": 0.3 } }""", "$.sockets[1].joint.pitch_axis", "required for an aimed joint")]
    [InlineData("""{ "id": "s", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "pitch_axis": [0, 1, 0], "pitch_min": -0.3, "pitch_max": 0.3 } }""", "$.sockets[1].joint.pitch_axis", "square to the axis")]
    [InlineData("""{ "id": "s", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "pitch_axis": [1, 0, 0], "pitch_min": -2, "pitch_max": 0.3 } }""", "$.sockets[1].joint.pitch_min", "within ±π/2")]
    [InlineData("""{ "id": "s", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "pitch_axis": [1, 0, 0], "pitch_min": 0.1, "pitch_max": 0.3 } }""", "$.sockets[1].joint.pitch_min", "include 0")]
    [InlineData("""{ "id": "s", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "pitch_axis": [1, 0, 0], "pitch_max": 0.3 } }""", "$.sockets[1].joint.pitch_min", "required for an aimed joint")]
    [InlineData("""{ "id": "s", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "pitch_axis": [1, 0, 0], "pitch_min": -0.3, "pitch_max": 0.3, "pivot": [0, 0.5, 0] } }""", "$.sockets[1].joint.pivot", "within the part's")]
    [InlineData("""{ "id": "s", "type": "aimed", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "min": 0.2, "max": 1, "pitch_axis": [1, 0, 0], "pitch_min": -0.3, "pitch_max": 0.3 } }""", "$.sockets[1].joint.min", "include 0")]
    [InlineData("""{ "id": "s", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "pitch_max": 0.3 } }""", "$.sockets[1].joint.pitch_max", "only aimed joints")]
    [InlineData("""{ "id": "s", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "inset": 0.25 } }""", "$.sockets[1].joint.inset", "less than one block")]
    [InlineData("""{ "id": "s", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "inset": -0.1 } }""", "$.sockets[1].joint.inset", "at least 0")]
    [InlineData("""{ "id": "s", "type": "linear", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "max": 1, "inset": 0.1 } }""", "$.sockets[1].joint.inset", "only rotational joints")]
    public void BadJointsGiveClearErrors(string socket, string path, string messagePart)
    {
        var error = Assert.Single(Check(socket), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(path, error.JsonPath);
        Assert.Contains(messagePart, error.Message);
    }

    [Fact]
    public void UnknownJointKeysWarn()
    {
        var warning = Assert.Single(Check("""{ "id": "s", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0], "joint": { "stifness": 1 } }"""));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("$.sockets[1].joint.stifness", warning.JsonPath);
    }

    [Theory]
    [InlineData("[0, -0.1, 0]", false)]
    [InlineData("[0, 0.2, 0]", true)]
    public void CentreOfMassMustBeInTheFootprint(string com, bool error)
    {
        var diagnostics = Check("""{ "id": "rotor", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0] }""", $", \"centre_of_mass\": {com}");
        Assert.Equal(error, diagnostics.Any(d => d.JsonPath == "$.centre_of_mass"));
    }

    [Fact]
    public void JointsRoundTripInSchemaOrder()
    {
        var part = new PartDefinition
        {
            Id = "s",
            Sockets = [new SocketDefinition
            {
                Id = "mount",
                Type = SocketType.Spring,
                Joint = new JointDefinition { Axis = new Vec3(0, 1, 0), Min = -0.1f, Max = 0.1f, Stiffness = 9000, Damping = 400 },
            }],
            CentreOfMass = new Vec3(0, -0.05f, 0),
        };
        var json = DataJson.Write(part);
        Assert.Contains("\"centre_of_mass\"", json);
        Assert.True(json.IndexOf("\"axis\"", StringComparison.Ordinal) < json.IndexOf("\"damping\"", StringComparison.Ordinal));
        Assert.Equal(json, DataJson.Write(DataJson.Read<PartDefinition>(json)!));
    }
}
