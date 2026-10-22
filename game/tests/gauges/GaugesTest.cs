using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Survival;
using Mechanica.Core.Ui;
using Mechanica.Core.World;
using Mechanica.Game.Ui;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The vehicle gauges, host plus a client in Survival. The host builds a floor by the client with a passenger seat, two Gas
/// Containers and a Battery Container. Standing, the client sees no gauges. Seated, its HUD shows Gas 1 and Gas 2 over
/// Battery 1 with what each holds; the host pours gasoline into Gas 2 and the client's row follows. The client renames
/// Gas 2 "Main tank" in its window: the host's part carries the name and the client's gauge shows it. One row per type
/// sums the two tanks with no name; Off hides the panel. Standing up takes the gauges away.
/// </summary>
public partial class GaugesTest : StagedTest
{
    private const string Seat = "vanilla:scrap_seat";
    private const string Tank = "vanilla:gas_container";
    private const string Battery = "vanilla:battery";
    private const string Fuel = "vanilla:gasoline";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private VehicleGaugesHud _hud = null!;
    private Items.InventoryScreen _screen = null!;
    private VehicleGaugeMode _mode = VehicleGaugeMode.PerContainer;
    private int _floor;
    private int _seat;
    private int _tank1;
    private int _tank2;
    private int _battery;

    protected override string Tag => "gauges";

    protected override int StageTimeoutFrames => 900;

    private Creation HostFloor => _host.Main.Creations.Find(_floor)!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Inventory.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor() && _host.Main.Inventory.Items is not null);
        Add("standing by the cart, the client sees no gauges", frame =>
        {
            if (frame == 0)
            {
                Build();
                _hud = new VehicleGaugesHud { Name = "Gauges", Gauges = _client.Main.Gauges, Mode = () => _mode };
                AddChild(_hud);
                _screen = new Items.InventoryScreen
                {
                    Name = "InventoryScreen", Inventory = _client.Main.Inventory, Actions = _client.Main.Actions, Players = _client.Main.PlayerSync,
                    Catalog = _client.Main.Build.Catalog, Hotbar = _client.Main.Hotbar, Creations = _client.Main.Creations,
                };
                AddChild(_screen);
            }
            if (frame < 30 || _client.Main.Creations.Find(_floor) is null)
                return false;
            Check(!_hud.Showing && _client.Main.Gauges.Rows.Count == 0, "no gauges while standing");
            return true;
        });
        Add("seated, the client's HUD shows Gas 1 and Gas 2 over Battery 1", frame =>
        {
            if (frame == 0)
                Request(LogicActions.EnterSeat, new LogicPartArgs(_floor, _seat));
            if (!_hud.Showing || _hud.Shown.Count() != 3)
                return false;
            Check(_results.All(r => r.Ok), $"the client sits ({string.Join("; ", _results)})");
            _results.Clear();
            var rows = _hud.Shown.ToList();
            Check(rows[0] == (GaugeKind.Gas, "Gas 1", "20 / 100", false), $"Gas 1 is first ({rows[0]})");
            Check(rows[1] == (GaugeKind.Gas, "Gas 2", "5 / 100", true), $"Gas 2 is under it and low ({rows[1]})");
            Check(rows[2] == (GaugeKind.Electric, "Battery 1", "30 / 100", false), $"Battery 1 is below the gas ({rows[2]})");
            return true;
        });
        Add("gasoline poured into Gas 2 on the host shows on the client's row", frame =>
        {
            if (frame == 0)
                HostFloor.InventoryOf(_tank2)!.Add(Fuel, 40);
            return _hud.Shown.ElementAt(1) == (GaugeKind.Gas, "Gas 2", "45 / 100", false);
        });
        Add("the client renames Gas 2 in its window and its gauge shows the name", frame =>
        {
            if (frame == 0)
                _screen.Show(InventoryRef.OfPart(_floor, _tank2));
            if (frame == 30)
            {
                Check(_screen.ContainerRenameButton.Visible, "the window has a rename button");
                Check(_screen.ContainerHeader.Title == "Gas 2", $"the window is titled with its name ({_screen.ContainerHeader.Title})");
                _screen.StartRename();
                Check(_screen.Renaming && _screen.ContainerNameField.Text == "Gas 2", "the name turns into a field");
                _screen.ContainerNameField.Text = "Main tank";
                _screen.FinishRename(save: true);
            }
            if (frame < 30 || HostFloor.FindPart(_tank2)!.Name != "Main tank" || _hud.Shown.ElementAt(1).Name != "Main tank")
                return false;
            Check(_screen.ContainerHeader.Title == "Main tank", $"the window shows the new name ({_screen.ContainerHeader.Title})");
            _screen.Hide();
            return true;
        });
        Add("one row per type sums the tanks with no name, and Off hides them", frame =>
        {
            if (frame == 0)
                _mode = VehicleGaugeMode.PerType;
            if (frame == 5)
            {
                var rows = _hud.Shown.ToList();
                Check(rows.Count == 2 && rows[0] == (GaugeKind.Gas, "", "65 / 200", false) && rows[1] == (GaugeKind.Electric, "", "30 / 100", false),
                    $"one gas row and one battery row ({string.Join("; ", rows)})");
                _mode = VehicleGaugeMode.Off;
            }
            if (frame < 10)
                return false;
            Check(!_hud.Showing, "Off hides the gauges");
            _mode = VehicleGaugeMode.PerContainer;
            return true;
        });
        Add("standing up takes the gauges away", frame =>
        {
            if (frame == 0)
                Request(LogicActions.LeaveSeat, new NoArgs());
            return _client.Main.Gauges.Rows.Count == 0 && !_hud.Showing;
        });
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    // A metal floor in front of the client with a passenger seat, two Gas Containers (20 and 5 gasoline) and a Battery
    // Container (30 batteries).
    private void Build()
    {
        var catalog = _host.Main.Build.Catalog;
        var creation = new Creation(_host.Main.Build.NextCreationId(), catalog);
        int Put(string part, int x, int y, int z)
        {
            if (!creation.TryPlacePart(part, new GridCell(x, y, z), default, out var placed, out var error))
                Check(false, $"{part} fits ({error})");
            return placed?.Id ?? 0;
        }
        for (var x = 0; x < 12; x++)
            for (var z = 0; z < 6; z++)
                Put("vanilla:metal_block", x, 0, z);
        _seat = Put(Seat, 0, 1, 1);
        _tank1 = Put(Tank, 4, 1, 1);
        _battery = Put(Battery, 8, 1, 1);
        _tank2 = Put(Tank, 4, 1, 4);
        creation.InventoryOf(_tank1)!.Add(Fuel, 20);
        creation.InventoryOf(_tank2)!.Add(Fuel, 5);
        creation.InventoryOf(_battery)!.Add("vanilla:battery_pack", 30);
        var feet = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
        creation.SetAllBodyPoses(new Pose((feet + new Vector3(-0.6f, 0.25f, -2.2f)).ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Build.Added(creation);
        _floor = creation.Id;
    }
}
