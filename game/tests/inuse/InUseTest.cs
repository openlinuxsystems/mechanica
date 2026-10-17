using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Net;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Ui;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// "In use" indicators (UI spec §11, PR 15), host plus a client in Survival. The host builds a floor between the
/// two players with a chest and a seat. When the host opens the chest the client sees an icon over it naming the
/// host, and the host sees none for its own use; with both looking, each sees the other; when the host closes it
/// the client's icon goes. The client sitting in the seat and placing its Lift mark both on the host's screen,
/// Hide GUI hides every icon, and standing up clears the seat's.
/// </summary>
public partial class InUseTest : StagedTest
{
    private const string Chest = "vanilla:chest";
    private const string Seat = "vanilla:scrap_seat";
    private const string Floor = "vanilla:wood_block";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private InUseIndicators _hostMarks = null!;
    private InUseIndicators _clientMarks = null!;
    private int _workshop;
    private int _chest;
    private int _seat;

    protected override string Tag => "inuse";

    protected override int StageTimeoutFrames => 600;

    private InventoryRef ChestRef => InventoryRef.OfPart(_workshop, _chest);

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Inventory.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor()
            && _host.Main.Inventory.Items is not null && _client.Main.PlayerSync.Players.Count == 2);
        Add("the host builds a chest and a seat between the players", frame =>
        {
            if (frame == 0)
            {
                Build();
                _hostMarks = Marks(_host);
                _clientMarks = Marks(_client);
            }
            return _client.Main.Creations.Find(_workshop) is { } seen && seen.Parts.Count == _host.Main.Creations.Find(_workshop)!.Parts.Count;
        });
        Wait("the floor settles", 30);
        Add("nobody uses anything, so nothing is marked", _ =>
        {
            Check(!_hostMarks.Shown.Any() && !_clientMarks.Shown.Any(), "no icons");
            return true;
        });
        Add("the host opens the chest and the client sees it in use", frame =>
        {
            if (frame == 0)
                _host.Main.Inventory.Open(ChestRef);
            if (Shown(_clientMarks, InUseKind.Open, _chest) is not { } mark)
                return false;
            Check(mark.Text == "In use: Host", $"the icon names the host ({mark.Text})");
            Check(mark.Mark.Peers.SequenceEqual([_host.Peer]), "only the host uses it");
            var centre = Logic.ConnectionNodes.Centre(_client.Main.Creations.NodeOf(_workshop)!, _chest)!.Value;
            Check(mark.Position.Y > centre.Y + 0.3f && new Vector2(mark.Position.X - centre.X, mark.Position.Z - centre.Z).Length() < 0.05f,
                $"it sits over the chest (icon {mark.Position}, chest {centre})");
            Check(!_hostMarks.Shown.Any(), "the host sees no icon for its own use");
            return true;
        });
        Add("with both looking, each sees the other", frame =>
        {
            if (frame == 0)
                _client.Main.Inventory.Open(ChestRef);
            if (Shown(_hostMarks, InUseKind.Open, _chest) is not { } mark)
                return false;
            Check(mark.Text == "In use: Client", $"the host's icon names the client ({mark.Text})");
            Check(Shown(_clientMarks, InUseKind.Open, _chest)?.Text == "In use: Host", "the client's still names only the host");
            return true;
        });
        Add("the host closes the chest and the client's icon goes", frame =>
        {
            if (frame == 0)
            {
                _host.Main.Inventory.Close(ChestRef);
                _client.Main.Inventory.Close(ChestRef);
            }
            return Shown(_clientMarks, InUseKind.Open, _chest) is null && Shown(_hostMarks, InUseKind.Open, _chest) is null;
        });
        Add("the client sits down and the host sees the seat in use", frame =>
        {
            if (frame == 0)
                Request(LogicActions.EnterSeat, new LogicPartArgs(_workshop, _seat));
            if (_results.Count == 0)
                return false;
            if (!Check(_results[0].Ok, $"the client sits ({_results[0].Message})"))
                return true;
            if (Shown(_hostMarks, InUseKind.Seat, _seat) is not { } mark)
                return false;
            Check(mark.Text == "In use: Client", $"the seat's icon names the client ({mark.Text})");
            Check(Shown(_clientMarks, InUseKind.Seat, _seat) is null, "the client sees none on its own seat");
            _results.Clear();
            return true;
        });
        Add("the client's Lift is marked on the host", frame =>
        {
            if (frame == 0)
            {
                var me = _client.Main.LocalPlayer!.GlobalPosition;
                Request(ToolActions.PlaceLift, new PlaceLiftArgs(new Vec3(me.X + 2.5f, me.Y, me.Z + 2.5f)));
            }
            if (_results.Count == 0)
                return false;
            if (!Check(_results[0].Ok, $"the client places its Lift ({_results[0].Message})"))
                return true;
            if (Shown(_hostMarks, InUseKind.Lift, _client.Peer) is not { } mark)
                return false;
            var top = _host.Main.Build.Lifts[_client.Peer].Top;
            Check(mark.Text == "In use: Client" && mark.Position.Y > top.Y, $"over the Lift, naming the client ({mark.Text}, {mark.Position} over {top})");
            Check(Shown(_clientMarks, InUseKind.Lift, _client.Peer) is null, "the client sees none on its own Lift");
            _results.Clear();
            return true;
        });
        Add("Hide GUI hides the icons", frame =>
        {
            if (frame == 0)
                HudRoot.Hidden = true;
            if (frame < 3)
                return false;
            Check(!_hostMarks.Shown.Any(), "no icons while the GUI is hidden");
            HudRoot.Hidden = false;
            return true;
        });
        Add("standing up clears the seat's icon", frame =>
        {
            if (frame == 0)
                Request(LogicActions.LeaveSeat, new NoArgs());
            return Shown(_hostMarks, InUseKind.Seat, _seat) is null && Shown(_hostMarks, InUseKind.Lift, _client.Peer) is not null;
        });
    }

    private static (InUseMark Mark, Vector3 Position, string Text)? Shown(InUseIndicators marks, InUseKind kind, int part) =>
        marks.Shown.Where(m => m.Mark.Kind == kind && m.Mark.Part == part).Select(m => ((InUseMark, Vector3, string)?)m).FirstOrDefault();

    private static InUseIndicators Marks(Instance instance)
    {
        var marks = new InUseIndicators
        {
            Name = "InUse", Session = instance.Main.Session, Players = instance.Main.PlayerSync, Inventory = instance.Main.Inventory,
            Logic = instance.Main.Logic, Build = instance.Main.Build, Creations = instance.Main.Creations,
        };
        instance.Main.AddChild(marks);
        return marks;
    }

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _results.Add);

    // A wooden floor 8 by 8 blocks halfway between the players, a chest and a seat on it.
    private void Build()
    {
        var catalog = _host.Main.Build.Catalog;
        var creation = new Creation(_host.Main.Build.NextCreationId(), catalog);
        for (var x = 0; x < 8; x++)
        {
            for (var z = 0; z < 8; z++)
            {
                if (!creation.TryPlacePart(Floor, new GridCell(x, 0, z), default, out _, out var error))
                    Check(false, $"the floor builds ({error})");
            }
        }
        _chest = PlaceOnFloor(creation, Chest, 0);
        _seat = PlaceOnFloor(creation, Seat, 5);
        var host = _host.Main.PlayerSync.Players[_host.Peer].GlobalPosition;
        var client = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
        var middle = (host + client) / 2 + new Vector3(-1, 0, -2);
        creation.SetAllBodyPoses(new Pose(middle.ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Build.Added(creation);
        _workshop = creation.Id;
    }

    // Puts a part on top of the floor with its lowest x cell at x, trying cells until its base socket attaches.
    private int PlaceOnFloor(Creation creation, string part, int x)
    {
        var definition = _host.Main.Build.Catalog.FindDefinition(part)!;
        var origin = PartGeometry.Cells(definition, GridCell.Zero, default).ToList();
        var (minX, minY, minZ) = (origin.Min(c => c.X), origin.Min(c => c.Y), origin.Min(c => c.Z));
        for (var z = 0; z < 4; z++)
        {
            var cell = new GridCell(x - minX, 1 - minY, z - minZ);
            if (creation.TryPlacePart(part, cell, default, out var placed, out _))
                return placed.Id;
        }
        Check(false, $"{part} fits on the floor at x {x}");
        return 0;
    }
}
