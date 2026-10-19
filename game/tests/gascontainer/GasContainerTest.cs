using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Gas Container, host plus a client in Survival. The host builds a floor with a Gas Container and a Gas Engine by
/// the client. The client sees the container's model with its gauge empty; the host fills it with 40 gasoline and the
/// client's gauge rises to 0.4 with a pulse. The client links it to the engine with the Connect Tool and switches the
/// engine on: the engine burns from it, and the client sees the supply clip and the gauge drop. Then the client picks
/// the container up while it still holds fuel and gets the container and its gasoline.
/// </summary>
public partial class GasContainerTest : StagedTest
{
    private const string Container = "vanilla:gas_container";
    private const string Engine = "vanilla:gas_engine";
    private const string Fuel = "vanilla:gasoline";
    private const string Floor = "vanilla:wood_block";
    private const string Connector = "vanilla:connect_tool";

    private readonly List<ActionResult> _results = [];
    private readonly HashSet<string> _clips = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _floor;
    private int _tank;
    private int _engine;

    protected override string Tag => "gascontainer";

    protected override int StageTimeoutFrames => 900;

    private Creation HostFloor => _host.Main.Creations.Find(_floor)!;

    private Inventory HostPockets => _host.Main.Inventory.Items!.PlayerInventory(_client.Peer);

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Inventory.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor() && _host.Main.Inventory.Items is not null);
        Add("the client sees the Gas Container with its gauge empty", frame =>
        {
            if (frame == 0)
                Build();
            if (Display(_client) is not { } visual || visual.Clip is null || Gauge(_client) is not { } gauge)
                return false;
            Check(visual.Player is not null && visual.Player.HasAnimation("supply") && visual.Player.HasAnimation("fill"),
                "the model brings its clips");
            Check(visual.Clip == "rest", $"it rests while empty ({visual.Clip})");
            Check(gauge < 0.05f, $"the gauge is empty ({gauge:F2})");
            return true;
        });
        Add("filled with 40 gasoline, the client's gauge pulses and rises to 0.4", frame =>
        {
            if (frame == 0)
                HostFloor.InventoryOf(_tank)!.Add(Fuel, 40);
            if (Display(_client)?.Clip is { } shown)
                _clips.Add(shown);
            if (Gauge(_client) is not { } gauge || Mathf.Abs(gauge - 0.4f) > 0.02f || Display(_client)!.Clip != "idle_loop")
                return false;
            Check(_clips.Contains("pulse"), $"it pulsed ({string.Join(", ", _clips)})");
            _clips.Clear();
            return true;
        });
        Add("linked and switched on, the engine burns from it and the client sees the supply", frame =>
        {
            if (frame == 0)
            {
                HostPockets.Add(Connector, 1);
                return false;
            }
            if (frame == 20)
            {
                Request(LogicActions.ConnectParts, new LinkPartsArgs(_floor, _tank, _engine));
                Request(LogicActions.ConfigurePart, new ConfigurePartArgs(_floor, _engine,
                    new Dictionary<string, JsonElement?> { ["on"] = JsonSerializer.SerializeToElement(true) }));
            }
            if (Display(_client)?.Clip is { } shown)
                _clips.Add(shown);
            if (frame < 20 || HostFloor.InventoryOf(_tank)!.Count(Fuel) != 39 || !_clips.Contains("supply")
                || Gauge(_client) is not { } gauge || Mathf.Abs(gauge - 0.39f) > 0.02f)
                return false;
            Check(_results.All(r => r.Ok), $"linking and switching on succeed ({string.Join("; ", _results)})");
            _results.Clear();
            return true;
        });
        Add("the client picks it up full and gets it with its gasoline", frame =>
        {
            if (frame == 0)
            {
                Request(LogicActions.ConfigurePart, new ConfigurePartArgs(_floor, _engine,
                    new Dictionary<string, JsonElement?> { ["on"] = JsonSerializer.SerializeToElement(false) }));
                return false;
            }
            if (frame == 60)
                Request(BuildActions.RemovePart, new RemovePartArgs(_floor, _tank));
            if (frame < 60 || _results.Count < 2 || _client.Main.Inventory.Own?.Count(Container) != 1)
                return false;
            Check(_results.All(r => r.Ok), $"switching off and picking up succeed ({string.Join("; ", _results)})");
            var fuel = _client.Main.Inventory.Own!.Count(Fuel);
            Check(fuel == HostPockets.Count(Fuel) && fuel is >= 38 and <= 39, $"the gasoline came along ({fuel})");
            Check(HostFloor.FindPart(_tank) is null, "the container is gone from the floor");
            return true;
        });
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private PartDisplayVisual? Display(Instance instance) =>
        instance.Main.Creations.NodeOf(_floor)?.Displays.FirstOrDefault(d => d.Part == _tank);

    // How full the gauge on the model shows the container: the fuel column's height.
    private float? Gauge(Instance instance) =>
        instance.Main.Creations.NodeOf(_floor)?.PartNode(_tank)?.FindChild("LiquidL", true, false) is Node3D column ? column.Scale.Y : null;

    // A wooden floor 7 by 6 blocks in front of the client with a Gas Container and a Gas Engine 1 on it.
    private void Build()
    {
        var catalog = _host.Main.Build.Catalog;
        var creation = new Creation(_host.Main.Build.NextCreationId(), catalog);
        for (var x = 0; x < 7; x++)
        {
            for (var z = 0; z < 6; z++)
            {
                if (!creation.TryPlacePart(Floor, new GridCell(x, 0, z), default, out _, out var error))
                    Check(false, $"the floor builds ({error})");
            }
        }
        if (!creation.TryPlacePart(Container, new GridCell(0, 1, 1), default, out var tank, out var why))
            Check(false, $"the container fits ({why})");
        if (!creation.TryPlacePart(Engine, new GridCell(5, 1, 1), default, out var engine, out why))
            Check(false, $"the engine fits ({why})");
        _tank = tank?.Id ?? 0;
        _engine = engine?.Id ?? 0;
        var feet = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
        creation.SetAllBodyPoses(new Pose((feet + new Vector3(-0.9f, 0.25f, -2.5f)).ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Build.Added(creation);
        _floor = creation.Id;
    }
}
