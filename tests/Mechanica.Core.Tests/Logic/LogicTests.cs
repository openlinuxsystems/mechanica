using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Logic;

/// <summary>Logic links, part settings, the signal bus, seats and the logic actions (chunk 10).</summary>
public partial class LogicTests
{
    private const string Block = "test:block";
    private const string Source = "test:source";
    private const string Lamp = "test:lamp";
    private const string Gate = "test:gate";
    private const string Seat = "test:seat";
    private const string Switch = "test:switch";
    private const string Sequencer = "test:sequencer";
    private const int Host = Creation.HostPeer, Peer = 2;

    private static readonly ActionCaller Player = new(Peer, ActionSource.Player);

    private static readonly SettingDefinition Mode = new()
    {
        Id = "mode", Name = "Mode", Type = SettingType.Choice, Options = ["and", "or"], Default = Json("and"),
    };

    private static readonly SettingDefinition Delay = new()
    {
        Id = "delay", Name = "Delay", Type = SettingType.Number, Min = 0, Max = 10, Step = 0.5f, Default = Json(1),
    };

    private static readonly SettingDefinition Frames = new()
    {
        Id = "frames", Name = "Frames", Type = SettingType.NumberList, Min = -90, Max = 90, Step = 1, MaxCount = 3,
    };

    private static readonly SettingDefinition Steps = new()
    {
        Id = "frames", Name = "Frames", Type = SettingType.Sequence, Min = -90, Max = 90, Step = 1, MaxCount = 3, MaxSpeed = 5, Preview = "preview",
    };

    private static readonly PartCatalog Catalog = new PartCatalog()
        .Add(Block, Part(null))
        .Add(Source, Part(new LogicDefinition { Outputs = 2 }))
        .Add(Lamp, Part(new LogicDefinition { Inputs = 1 }))
        .Add(Gate, Part(new LogicDefinition { Inputs = 4, Outputs = 4 }) with
        {
            Settings = [Mode, Delay, Frames, new SettingDefinition { Id = "loop", Name = "Loop", Type = SettingType.Toggle }],
        })
        .Add(Seat, Part(new LogicDefinition { Outputs = 4 }) with { Seat = new SeatDefinition() })
        .Add(Switch, Part(new LogicDefinition { Inputs = 1, Outputs = 4, Use = "toggle" }))
        .Add(Sequencer, Part(new LogicDefinition { Inputs = 4, Outputs = 4 }) with { Settings = [Steps] });

    private readonly FakeWorld _world = new();
    private readonly FakeHost _host = new();
    private readonly SeatRegistry _seats = new();
    private readonly ActionDispatcher _dispatcher = new();
    private readonly LogicActions _actions;

    public LogicTests()
    {
        _actions = new LogicActions(new BuildSettings { Reach = 4f }, _world, _host, _seats);
        _actions.Register(_dispatcher);
        _world.Eyes[Peer] = new Vector3(0, 1, 2);
    }

    // A row of parts along X from the origin, all in one Body.
    private Creation Row(params string[] parts)
    {
        var creation = new Creation(1, Catalog);
        for (var i = 0; i < parts.Length; i++)
            Assert.True(creation.TryPlacePart(parts[i], new GridCell(i, 0, 0), default, out _, out var error), error);
        _world.Creations[creation.Id] = creation;
        return creation;
    }

    [Fact]
    public void LinksGoFromAnOutputToAnInputWithinTheirLimits()
    {
        var c = Row(Source, Lamp, Lamp, Lamp, Block);
        Assert.True(c.TryLink(1, 2, out var error), error);
        Assert.Contains("already feeds", c.CheckLink(1, 2));
        Assert.Contains("itself", c.CheckLink(1, 1));
        Assert.Contains("no output", c.CheckLink(2, 1));
        Assert.Contains("takes no input", c.CheckLink(1, 5));
        Assert.Contains("no part 9", c.CheckLink(1, 9));
        Assert.True(c.TryLink(1, 3, out error), error);
        Assert.Contains("all 2 of its output links", c.CheckLink(1, 4));
        Assert.Equal([2, 3], c.LinksFrom(1));
        Assert.Equal([1], c.LinksTo(2));
        var revision = c.Revision;
        Assert.True(c.Unlink(1, 2));
        Assert.False(c.Unlink(1, 2));
        Assert.Equal(revision + 1, c.Revision);
        Assert.Equal([new LogicLink(1, 3)], c.Links);
    }

    [Fact]
    public void RemovingAPartTakesItsLinksAndASplitKeepsOnlyLinksWithinEachPiece()
    {
        var c = Row(Source, Lamp, Block, Lamp, Gate);
        foreach (var (from, to) in new[] { (1, 2), (1, 5), (5, 4) })
            Assert.True(c.TryLink(from, to, out var error), error);
        var next = 10;
        var result = c.RemovePart(3, () => next++);
        var piece = Assert.Single(result.SplitOff);
        // The link across the gap is gone; each side keeps its own.
        Assert.Equal([new LogicLink(1, 2)], c.Links);
        Assert.Equal([new LogicLink(5, 4)], piece.Links);

        c.RemovePart(2, () => next++);
        Assert.Empty(c.Links);
    }

    [Fact]
    public void LinksAndSettingsRoundTripThroughSaves()
    {
        var c = Row(Source, Gate, Lamp);
        c.TryLink(1, 2, out _);
        c.TryLink(2, 3, out _);
        Assert.True(c.SetPartSettings(2, PartSettings.Apply(Catalog.FindDefinition(Gate)!, null, Changes(("mode", Json("or")), ("delay", Json(2.5))))));
        var json = CreationJson.Write(c);
        var diagnostics = new List<DataDiagnostic>();
        var back = CreationJson.Read(json, Catalog, "c.json", diagnostics);
        Assert.Empty(diagnostics);
        Assert.Equal(c.Links, back!.Links);
        Assert.Equal("or", PartSettings.Value(back.DefinitionOf(2), back.FindPart(2)!.Settings, "mode")!.Value.GetString());
        Assert.Equal(2.5, PartSettings.Value(back.DefinitionOf(2), back.FindPart(2)!.Settings, "delay")!.Value.GetDouble());
        Assert.Equal(json, CreationJson.Write(back));
        Assert.Contains("\"links\"", json);
    }

    [Fact]
    public void SavesWithBadLinksOrSettingsAreRefused()
    {
        var c = Row(Source, Gate);
        c.TryLink(1, 2, out _);
        var json = CreationJson.Write(c);
        var diagnostics = new List<DataDiagnostic>();
        Assert.Null(CreationJson.Read(json.Replace("\"to\": 2", "\"to\": 1"), Catalog, "c.json", diagnostics));
        Assert.NotEmpty(diagnostics);

        c.SetPartSettings(2, PartSettings.Apply(c.DefinitionOf(2), null, Changes(("mode", Json("or")))));
        diagnostics.Clear();
        Assert.Null(CreationJson.Read(CreationJson.Write(c).Replace("\"or\"", "\"xor\""), Catalog, "c.json", diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains("must be one of"));
    }

    [Fact]
    public void SettingsKeepOnlyWhatDiffersFromTheDefault()
    {
        var gate = Catalog.FindDefinition(Gate)!;
        Assert.Null(PartSettings.Apply(gate, null, Changes(("mode", Json("and")))));
        var stored = PartSettings.Apply(gate, null, Changes(("delay", Json(3)), ("loop", Json(true))));
        Assert.Equal(["delay", "loop"], stored!.Keys);
        Assert.Null(PartSettings.Apply(gate, stored, Changes(("delay", null), ("loop", Json(false)))));
        Assert.Equal(["mode", "delay", "frames", "loop"], PartSettings.All(gate, stored).Select(s => s.Id));
        Assert.Equal(JsonValueKind.Array, PartSettings.Value(gate, null, "frames")!.Value.ValueKind);
        Assert.Null(PartSettings.Value(gate, null, "nope"));
    }

    [Theory]
    [InlineData("mode", "\"or\"", null)]
    [InlineData("mode", "\"xor\"", "must be one of and, or")]
    [InlineData("mode", "1", "must be one of")]
    [InlineData("delay", "10", null)]
    [InlineData("delay", "11", "between 0 and 10")]
    [InlineData("delay", "\"1\"", "must be a number")]
    [InlineData("frames", "[0, 45, -90]", null)]
    [InlineData("frames", "[0, 45, -90, 10]", "at most 3")]
    [InlineData("frames", "[100]", "between -90 and 90")]
    [InlineData("loop", "true", null)]
    [InlineData("loop", "1", "true or false")]
    public void SettingValuesAreChecked(string id, string value, string? error)
    {
        var gate = Catalog.FindDefinition(Gate)!;
        var problem = PartSettings.Check(PartSettings.Find(gate, id)!, JsonDocument.Parse(value).RootElement);
        if (error is null)
            Assert.Null(problem);
        else
            Assert.Contains(error, problem);
    }

    [Fact]
    public void ASignalTakesOneStepPerLinkAndInputsAddUp()
    {
        var c = Row(Source, Source, Gate, Lamp);
        c.TryLink(1, 3, out _);
        c.TryLink(2, 3, out _);
        c.TryLink(3, 4, out _);
        var bus = new SignalBus();
        bus.SetOutput(1, 1, 1);
        bus.SetOutput(1, 2, 0.5f);
        Assert.Equal([(1, 3, 1.5f)], bus.Step([c]));
        Assert.Equal(1.5f, bus.Input(1, 3));
        Assert.Equal([(2, 0.5f), (1, 1f)], bus.Inputs(c, 3).OrderByDescending(i => i.Part));
        Assert.Empty(bus.Step([c]));
        bus.SetOutput(1, 3, 1);
        Assert.Equal([(1, 4, 1f)], bus.Step([c]));
        // A link going away brings the input back to 0, once.
        c.Unlink(3, 4);
        Assert.Equal([(1, 4, 0f)], bus.Step([c]));
        Assert.Empty(bus.Step([c]));
        bus.SetOutput(1, 1, float.NaN);
        Assert.Equal(0f, bus.Output(1, 1));
        Assert.Equal([(1, 3, 0.5f)], bus.Step([c]));
    }

    [Fact]
    public void ASeatHoldsOnePlayerAndAPlayerOneSeat()
    {
        var seats = new SeatRegistry();
        Assert.True(seats.Sit(2, 1, 5));
        Assert.False(seats.Sit(3, 1, 5));
        Assert.True(seats.Sit(2, 1, 6));
        Assert.Null(seats.Occupant(1, 5));
        Assert.Equal(2, seats.Occupant(1, 6));
        Assert.Equal(new SeatAssignment(2, 1, 6), seats.Leave(2));
        Assert.Null(seats.Leave(2));
        Assert.Empty(seats.All);
    }

    [Fact]
    public void PlayersConnectPartsInReachAndHearIt()
    {
        var c = Row(Source, Lamp);
        var result = Dispatch(Player, LogicActions.ConnectParts, new LinkPartsArgs(1, 1, 2));
        Assert.True(result.Ok, result.ToString());
        Assert.Equal(1, result.Value!.Value.GetProperty("links").GetInt32());
        Assert.Equal([new LogicLink(1, 2)], c.Links);
        Assert.Equal(["connect_success"], _world.Sounds);
        Assert.Empty(_world.Log);
        Assert.Equal("Part 1 already feeds part 2.", Dispatch(Player, LogicActions.ConnectParts, new LinkPartsArgs(1, 1, 2)).Message);
        Assert.True(Dispatch(Player, LogicActions.DisconnectParts, new LinkPartsArgs(1, 1, 2)).Ok);
        Assert.Empty(c.Links);
        Assert.False(Dispatch(Player, LogicActions.DisconnectParts, new LinkPartsArgs(1, 1, 2)).Ok);

        _world.Eyes[Peer] = new Vector3(0, 1, 20);
        Assert.Contains("you can reach 4.0 m", Dispatch(Player, LogicActions.ConnectParts, new LinkPartsArgs(1, 1, 2)).Message);
        // Bots and agents have no reach.
        Assert.True(Dispatch(new ActionCaller(Host, ActionSource.Bot), LogicActions.ConnectParts, new LinkPartsArgs(1, 1, 2)).Ok);
    }

    [Fact]
    public void InSurvivalLinkingNeedsACraftedConnectTool()
    {
        var c = Row(Source, Lamp);
        var costs = new ToolCosts();
        var dispatcher = new ActionDispatcher();
        new LogicActions(new BuildSettings { Reach = 4f }, _world, _host, _seats) { Costs = costs }.Register(dispatcher);
        ActionResult Send(string action) => dispatcher.Dispatch(Player, action, JsonSerializer.Serialize(new LinkPartsArgs(1, 1, 2), ActionJson.Options));

        Assert.Equal("Craft a Connect Tool at a Fabricator first.", Send(LogicActions.ConnectParts).Message);
        Assert.Empty(c.Links);
        costs.Carried = true;
        Assert.True(Send(LogicActions.ConnectParts).Ok);
        costs.Carried = false;
        Assert.Equal("Craft a Connect Tool at a Fabricator first.", Send(LogicActions.DisconnectParts).Message);
        Assert.Equal([new LogicLink(1, 2)], c.Links);
        Assert.Equal([Mechanica.Core.Items.HotbarTools.Connect], costs.Asked.Distinct());
    }

    private sealed class ToolCosts : IBuildCosts
    {
        public bool Carried;
        public readonly List<string> Asked = [];

        public string? CheckPlace(ActionCaller caller, string part) => null;

        public void Placed(ActionCaller caller, string part)
        {
        }

        public void Removed(ActionCaller caller, string part, Vector3 at)
        {
        }

        public string? CheckTool(ActionCaller caller, string tool)
        {
            Asked.Add(tool);
            return Carried ? null : "Craft a Connect Tool at a Fabricator first.";
        }
    }

    [Fact]
    public void ConfigurePartChecksEveryValueBeforeChangingAny()
    {
        var c = Row(Gate);
        var bad = Dispatch(Player, LogicActions.ConfigurePart, new ConfigurePartArgs(1, 1, Changes(("mode", Json("or")), ("delay", Json(99)))));
        Assert.Contains("between 0 and 10", bad.Message);
        Assert.Null(c.FindPart(1)!.Settings);
        Assert.Contains("no setting 'speed'", Dispatch(Player, LogicActions.ConfigurePart, new ConfigurePartArgs(1, 1, Changes(("speed", Json(1))))).Message);

        var ok = Dispatch(Player, LogicActions.ConfigurePart, new ConfigurePartArgs(1, 1, Changes(("mode", Json("or")))));
        Assert.True(ok.Ok, ok.ToString());
        Assert.Equal("or", ok.Value!.Value.GetProperty("settings").GetProperty("mode").GetString());
        Assert.Equal("or", c.FindPart(1)!.Settings!["mode"].GetString());
        Assert.Equal(["logic_click"], _world.Sounds);

        Row(Lamp);
        Assert.Contains("has no settings", Dispatch(Player, LogicActions.ConfigurePart, new ConfigurePartArgs(1, 1, Changes(("mode", Json("or"))))).Message);
    }

    [Fact]
    public void ActivatePartAsksTheScriptHost()
    {
        Row(Source);
        _host.Refuse = "Source has nothing to use.";
        Assert.Equal("Source has nothing to use.", Dispatch(Player, LogicActions.ActivatePart, new LogicPartArgs(1, 1)).Message);
        _host.Refuse = null;
        var result = Dispatch(Player, LogicActions.ActivatePart, new LogicPartArgs(1, 1));
        Assert.True(result.Ok);
        Assert.Equal(1, result.Value!.Value.GetProperty("part").GetInt32()); // what on_activate returned
        Assert.Equal([(Peer, 1)], _host.Activated);
        Assert.Equal(["logic_activate"], _world.Sounds);
    }

    [Fact]
    public void PlayersSitInSeatsOneAtATimeAndStandUp()
    {
        Row(Seat, Seat, Lamp);
        _world.Eyes[3] = new Vector3(0, 1, 2);
        var changes = new List<(SeatAssignment, bool)>();
        _actions.SeatChanged += (seat, entered) => changes.Add((seat, entered));

        Assert.Contains("not a seat", Dispatch(Player, LogicActions.EnterSeat, new LogicPartArgs(1, 3)).Message);
        Assert.True(Dispatch(Player, LogicActions.EnterSeat, new LogicPartArgs(1, 1)).Ok);
        Assert.Equal("Someone is already sitting there.", Dispatch(new ActionCaller(3, ActionSource.Player), LogicActions.EnterSeat, new LogicPartArgs(1, 1)).Message);
        // Moving to the other seat stands the player up first.
        Assert.True(Dispatch(Player, LogicActions.EnterSeat, new LogicPartArgs(1, 2)).Ok);
        Assert.Equal(new SeatAssignment(Peer, 1, 2), _seats.SeatOf(Peer));
        Assert.True(Dispatch(Player, LogicActions.LeaveSeat, new NoArgs()).Ok);
        Assert.Equal("You are not sitting anywhere.", Dispatch(Player, LogicActions.LeaveSeat, new NoArgs()).Message);
        Assert.Equal([(new(Peer, 1, 1), true), (new(Peer, 1, 1), false), (new(Peer, 1, 2), true), (new(Peer, 1, 2), false)], changes);
        Assert.Equal(["enter 1", "leave 1", "enter 2", "leave 2"], _host.Seatings);
        Assert.Equal(["seat_enter", "seat_exit", "seat_enter", "seat_exit"], _world.Sounds);
    }

    [Fact]
    public void VacateStandsUpPlayersWhoseSeatIsGoneOrWhoCannotSit()
    {
        var c = Row(Seat, Seat);
        _seats.Sit(Peer, 1, 1);
        _seats.Sit(3, 1, 2);
        Assert.Empty(_actions.Vacate(_ => true));
        Assert.Equal([new SeatAssignment(3, 1, 2)], _actions.Vacate(peer => peer != 3));
        Assert.Equal(["leave 2"], _host.Seatings);
        c.RemovePart(1, () => 9);
        Assert.Equal([new SeatAssignment(Peer, 1, 1)], _actions.Vacate(_ => true));
        Assert.Empty(_seats.All);
    }

    [Fact]
    public void InspectCreationDescribesPartsSignalsAndSeats()
    {
        var c = Row(Source, Gate, Seat);
        c.TryLink(1, 2, out _);
        _host.Signals.SetOutput(1, 1, 1);
        _host.Signals.Step([c]);
        _seats.Sit(Peer, 1, 3);
        _host.Scripts[2] = ("scripts/gate.lua", "boom at line 3");
        c.SetPartDamage(2, 4.5f);
        var result = Dispatch(Player, LogicActions.InspectCreation, new InspectCreationArgs(1));
        Assert.True(result.Ok, result.ToString());
        var inspection = result.Value!.Value.Deserialize<CreationInspection>(ActionJson.Options)!;
        Assert.Equal(3, inspection.Parts.Count);
        Assert.Equal(1f, inspection.Parts[0].Output);
        Assert.Equal(1f, inspection.Parts[1].Input);
        Assert.Equal("and", inspection.Parts[1].Settings!["mode"].GetString());
        Assert.Equal("boom at line 3", inspection.Parts[1].Fault);
        Assert.Equal(Peer, inspection.Parts[2].Occupant);
        Assert.Equal(4.5f, inspection.Parts[1].Damage);
        Assert.Equal(0f, inspection.Parts[0].Damage);
        Assert.Equal([new LogicLink(1, 2)], inspection.Links);
        Assert.Equal(3f, inspection.Mass);
        Assert.Single(inspection.Bodies);
        Assert.Equal("There is no creation 5.", Dispatch(Player, LogicActions.InspectCreation, new InspectCreationArgs(5)).Message);
    }

    [Theory]
    [InlineData("\"logic\": { \"inputs\": 65 }", "$.logic.inputs", "0 to 64")]
    [InlineData("\"logic\": { \"use\": \"press\" }", "$.logic.use", "needs a script")]
    [InlineData("\"seat\": { \"offset\": [0, 1, 0] }", "$.seat.offset", "")]
    [InlineData("\"settings\": [{ \"id\": \"Bad Id\", \"type\": \"toggle\" }]", "$.settings[0].id", "")]
    [InlineData("\"settings\": [{ \"id\": \"a\" }]", "$.settings[0].type", "")]
    [InlineData("\"settings\": [{ \"id\": \"a\", \"type\": \"number\", \"min\": 2, \"max\": 1 }]", "$.settings[0].min", "")]
    [InlineData("\"settings\": [{ \"id\": \"a\", \"type\": \"choice\", \"options\": [\"x\", \"x\"] }]", "$.settings[0].options", "")]
    [InlineData("\"settings\": [{ \"id\": \"a\", \"type\": \"number\", \"min\": 0, \"max\": 1, \"default\": 5 }]", "$.settings[0].default", "between 0 and 1")]
    [InlineData("\"settings\": [{ \"id\": \"a\", \"type\": \"toggle\" }, { \"id\": \"a\", \"type\": \"toggle\" }]", "$.settings[1].id", "")]
    [InlineData("\"logic\": { \"inputs\": 1, \"accepts\": [\"power\", \"power\"] }", "$.logic.accepts", "twice")]
    [InlineData("\"logic\": { \"inputs\": 1, \"reverse\": \"spin\" }", "$.logic.reverse", "toggle settings")]
    [InlineData("\"logic\": { \"inputs\": 1, \"reverse\": \"a\" }, \"settings\": [{ \"id\": \"a\", \"type\": \"number\", \"min\": 0, \"max\": 1 }]", "$.logic.reverse", "toggle settings")]
    public void BadLogicDataGivesClearErrors(string json, string path, string messagePart)
    {
        using var mods = new TestMods().Mod("m");
        mods.Part("m", "p.json", TestMods.Block(overrides: $$"""{ "category": "interactive", {{json}} }"""));
        var errors = mods.Load().Diagnostics.Where(d => d.File == "mods/m/parts/p.json" && d.Severity == DiagnosticSeverity.Error).ToList();
        var error = Assert.Single(errors);
        Assert.Equal(path, error.JsonPath);
        Assert.Contains(messagePart, error.Message);
    }

    [Fact]
    public void LinksOnlyEndAtPartsThatTakeTheirSourcesKind()
    {
        var catalog = new PartCatalog()
            .Add("test:engine", Part(new LogicDefinition { Inputs = 1, Outputs = 2, Kind = PortKind.Power }))
            .Add("test:switch", Part(new LogicDefinition { Outputs = 2 }))
            .Add("test:frame", Part(new LogicDefinition { Outputs = 2, Kind = PortKind.Structural }))
            .Add("test:wheel", Part(new LogicDefinition { Inputs = 4, Kind = PortKind.Bearing, Accepts = [PortKind.Power] }))
            .Add("test:lamp", Part(new LogicDefinition { Inputs = 4 }));
        var c = new Creation(1, catalog);
        string[] parts = ["test:engine", "test:switch", "test:frame", "test:wheel", "test:lamp"];
        for (var i = 0; i < parts.Length; i++)
            Assert.True(c.TryPlacePart(parts[i], new GridCell(i, 0, 0), default, out _, out var error), error);
        Assert.Null(c.CheckLink(1, 4));
        Assert.Contains("takes no logic links", c.CheckLink(2, 4));
        Assert.Null(c.CheckLink(2, 5));
        Assert.Null(c.CheckLink(1, 5));
        Assert.Contains("takes no structural links", c.CheckLink(3, 5));
        Assert.Equal(PortKind.Logic, c.DefinitionOf(2).Logic!.KindOrDefault);
    }

    [Fact]
    public void APartIsReversedOnlyWhileItsReverseToggleIsOn()
    {
        var bearing = Part(new LogicDefinition { Inputs = 1, Kind = PortKind.Bearing, Reverse = "reverse" }) with
        {
            Settings = [new SettingDefinition { Id = "reverse", Name = "Reverse", Type = SettingType.Toggle }],
        };
        Assert.False(PartSettings.Reversed(bearing, null));
        Assert.True(PartSettings.Reversed(bearing, new Dictionary<string, JsonElement> { ["reverse"] = Json(true) }));
        Assert.False(PartSettings.Reversed(bearing with { Logic = bearing.Logic! with { Reverse = null } }, new Dictionary<string, JsonElement> { ["reverse"] = Json(true) }));
    }

    [Fact]
    public void LogicDataOnlyGoesOnPlaceableParts()
    {
        using var mods = new TestMods().Mod("m");
        mods.Part("m", "p.json", TestMods.Block(overrides: """{ "category": "resource", "size": null, "logic": { "outputs": 1 } }"""));
        Assert.Contains(mods.Load().Diagnostics, d => d.JsonPath == "$.logic" && d.Message.Contains("only placeable parts"));
    }

    private ActionResult Dispatch<T>(ActionCaller caller, string action, T args) => _dispatcher.Dispatch(caller, action, args);

    private static Dictionary<string, JsonElement?> Changes(params (string Id, JsonElement? Value)[] values) =>
        values.ToDictionary(v => v.Id, v => v.Value);

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static PartDefinition Part(LogicDefinition? logic) => new()
    {
        Id = "x", Name = "X", Category = PartCategory.Block, Mass = 1, Size = GridSize.One, Logic = logic,
    };

    private sealed class FakeHost : ILogicHost
    {
        public SignalBus Signals { get; } = new();
        public string? Refuse { get; set; }
        public List<(int Peer, int Part)> Activated { get; } = [];
        public List<string> Seatings { get; } = [];
        public Dictionary<int, (string?, string?)> Scripts { get; } = [];

        public string? CheckActivate(ActionCaller caller, Creation creation, int part) => Refuse;

        public JsonElement? Activate(ActionCaller caller, Creation creation, int part)
        {
            Activated.Add((caller.Peer, part));
            return Json(new { part });
        }

        public void Seated(SeatAssignment seat, bool entered) => Seatings.Add($"{(entered ? "enter" : "leave")} {seat.Part}");

        public (string? Script, string? Fault) ScriptOf(int creation, int part) => Scripts.GetValueOrDefault(part);

        public string? RefuseCommand { get; set; }

        public List<(int Peer, int Part, string Command, string? Args)> Commands { get; } = [];

        public string? CheckCommand(Creation creation, int part) => RefuseCommand;

        public void Command(ActionCaller caller, Creation creation, int part, string command, JsonElement? args) =>
            Commands.Add((caller.Peer, part, command, args?.GetRawText()));
    }

    private sealed class FakeWorld : IBuildWorld
    {
        public Dictionary<int, Creation> Creations { get; } = [];
        public Dictionary<int, Vector3> Eyes { get; } = [];
        public List<string> Log { get; } = [];
        public List<string> Sounds { get; } = [];

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public Vector3? EyeOf(int peer) => Eyes.TryGetValue(peer, out var eye) ? eye : null;

        public int NextCreationId() => 100;

        public string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose) => null;

        public void Added(Creation creation) => Log.Add($"added {creation.Id}");

        public void Changed(Creation creation) => Log.Add($"changed {creation.Id}");

        public void Removed(int creationId) => Log.Add($"removed {creationId}");

        public void PlaySound(string sound, string ownerModId, Vector3 position) => Sounds.Add(sound);
    }
}
