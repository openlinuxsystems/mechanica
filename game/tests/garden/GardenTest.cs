using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Parts map batch 16, host plus a client in Survival on flat ground. The host builds a base beside the client: a Small
/// Chest piped through two Vacuum Pipes to a Beehive's two-way port, and the eight pots in a row. The Beehive works on its
/// own (crafting.auto): a Pigment Flower put in the chest goes down the pipe into the hive, which works (its bees fly on
/// the client) and, three minutes on, sends a Beeswax back up the pipe into the chest, which the client sees. Then the
/// client puts a flower from its own pockets into the Beehive's slots and gets a second Beeswax in the chest the same way,
/// and places a Potted Cactus from its inventory on the base.
/// </summary>
public partial class GardenTest : StagedTest
{
    private const string Deck = "vanilla:metal_block";
    private const string Chest = "vanilla:small_chest";
    private const string Pipe = "vanilla:vacuum_pipe";
    private const string Hive = "vanilla:beehive";
    private const string Flower = "vanilla:pigment_flower";
    private const string Wax = "vanilla:beeswax";
    private const string Cactus = "vanilla:potted_cactus";
    private const string Recipe = "vanilla:beeswax_beehive";

    private static readonly string[] Pots =
    [
        "vanilla:big_pot", "vanilla:potted_plant", "vanilla:potted_plant_small", "vanilla:potted_seedling", "vanilla:potted_vine",
        "vanilla:potted_cactus_bloom", "vanilla:potted_blue_flower", Cactus,
    ];

    // Local +X turned to world -X: the Beehive's port faces back toward the chest.
    private static readonly GridRotation HalfTurn = GridRotation.FromAxes(new GridCell(-1, 0, 0), new GridCell(0, 1, 0));

    // Local +Z turned to world +X (a pipe running along x).
    private static readonly GridRotation AlongX = GridRotation.FromAxes(new GridCell(0, 0, -1), new GridCell(0, 1, 0));

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _base;
    private int _chest;
    private int _hive;
    private int _parts;

    protected override string Tag => "garden";

    protected override int StageTimeoutFrames => 900;

    private int Peer => _client.Peer;

    private Creation HostBase => _host.Main.Survival.FindCreation(_base)!;

    private InventoryRef HiveRef => InventoryRef.OfPart(_base, _hive);

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Survival.Own is { Dead: false } && _client.Main.Inventory.Own is not null
            && _client.Main.LocalPlayer is { } player && player.IsOnFloor());
        Stage("the host builds the base: the chest piped to the Beehive's port, and the eight pots; the client sees it", frame =>
        {
            if (frame == 0)
                BuildBase();
            if (frame < 20 || _client.Main.Creations.Find(_base) is not { } seen)
                return false;
            Check(StorageLinks.InputsOf(HostBase, _hive).Contains(_chest), "the Beehive takes its inputs from the piped chest");
            Check(StorageLinks.OutputsOf(HostBase, _hive).Contains(_chest), "and sends what it makes back to it");
            Check(StorageLinks.InputsOf(seen, _hive).Contains(_chest), "the client works out the same network");
            var pots = seen.Parts.Count(p => Pots.Contains(p.Part));
            Check(pots == Pots.Length, $"the client sees the {Pots.Length} pots ({pots})");
            Check(seen.Parts.Count() == _parts, $"and every part ({seen.Parts.Count()} of {_parts})");
            return true;
        });
        Stage("the Beehive idles with nothing to work with", frame =>
        {
            if (frame < 60)
                return false;
            Check(HostBase.CraftingOf(_hive)!.IsEmpty, "no craft starts without a flower");
            return Check(Clip(_client, _hive) is null or "" or "idle_loop", $"its bees idle on the client ({Clip(_client, _hive)})");
        });
        Stage("a Pigment Flower in the chest goes down the pipe: the Beehive starts on its own and its bees fly on the client", frame =>
        {
            if (frame == 0)
            {
                HostBase.InventoryOf(_chest)!.Add(Flower, 1);
                Teleport(Beside(_hive));
            }
            if (frame == 30)
                _client.Main.Inventory.Open(HiveRef);
            if (frame < 30 || Shown(_client, HiveRef) is not { Queue: [var job] } || Clip(_client, _hive)?.Contains("work_loop") != true)
                return false;
            Check(job.Recipe == Recipe && job.Time == 180f, $"the client's Beehive menu shows the craft ({job.Recipe}, {job.Time} s)");
            Check(HostBase.InventoryOf(_chest)!.Count(Flower) == 0, "the flower left the chest");
            Check(Clip(_host, _hive)?.Contains("work_loop") == true, $"the host's Beehive works too ({Clip(_host, _hive)})");
            return true;
        });
        Stage("three minutes on, a Beeswax comes back up the pipe into the chest, and the client sees it", frame =>
        {
            if (frame == 0)
                _host.Main.Inventory.Items!.Tick(181f);
            if (frame == 10)
                RequestView(_chest);
            if (frame < 10 || _results.Count == 0)
                return false;
            Check(View(0)?.Count(Wax) == 1, $"the client sees the Beeswax in the chest ({_results[0]})");
            Check(HostBase.InventoryOf(_hive)!.Count(Wax) == 0, "the wax went out of the port, not into the hive's slots");
            Check(Clip(_client, _hive)?.StartsWith("complete", StringComparison.Ordinal) == true, $"its bees go home ({Clip(_client, _hive)})");
            return true;
        });
        Stage("the client puts a flower from its pockets in the Beehive: it works it into a second Beeswax in the chest", frame =>
        {
            if (frame == 0)
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Flower, 1);
            if (frame < 10)
                return false;
            if (frame == 10)
            {
                if (_client.Main.Inventory.Own?.Items.FirstOrDefault(s => s.Item == Flower) is not { } slot)
                    return Check(false, "the client has the flower");
                Request(InventoryActions.MoveItems, new MoveItemsArgs(InventoryRef.OfPlayer(Peer), slot.Slot, HiveRef));
            }
            if (_results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the flower goes into the Beehive ({_results[0]})");
            if (_results.Count == 1)
            {
                if (HostBase.CraftingOf(_hive)!.IsEmpty)
                    return false;
                Check(HostBase.InventoryOf(_hive)!.Count(Flower) == 0, "the Beehive took the flower from its own slot");
                _host.Main.Inventory.Items!.Tick(181f);
                RequestView(_chest);
                return false;
            }
            return Check(View(1)?.Count(Wax) == 2, $"the client sees two Beeswax in the chest ({_results[1]})");
        });
        Stage("the client places a Potted Cactus from its inventory on the base", frame =>
        {
            if (frame == 0)
            {
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Cactus, 1);
                Teleport(Beside(_chest));
                _parts = HostBase.Parts.Count();
            }
            if (frame == 30)
                Request(BuildActions.PlacePart, new PlacePartArgs(Cactus, new GridCell(-1, 1, -2), default, _base));
            if (frame < 30 || _results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the host places it ({_results[0]})");
            if (_client.Main.Creations.Find(_base) is not { } seen || seen.Parts.Count(p => p.Part == Cactus) != 2)
                return false;
            // The client's inventory syncs on its own schedule, apart from the creation's: give it a few seconds.
            if (Count(Cactus) != 0 && frame < 330)
                return false;
            Check(HostBase.Parts.Count() == _parts + 1, "the host's base has it");
            Check(Count(Cactus) == 0, "it left the client's inventory");
            return true;
        });
    }

    // A deck of blocks (x -2 to 12, z -2 to 14) 6 m in front of the client with: a Small Chest, two Vacuum Pipes on blocks
    // and a Beehive turned half round so its port meets the far pipe; the eight pots in two rows behind them.
    private void BuildBase()
    {
        var build = _host.Main.Build;
        var creation = new Creation(build.NextCreationId(), build.Catalog);
        for (var x = -2; x <= 12; x++)
        {
            for (var z = -2; z <= 14; z++)
                creation.TryPlacePart(Deck, new GridCell(x, 0, z), default, out _, out _);
        }
        int Place(string part, GridCell cell, GridRotation rotation = default)
        {
            if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var why))
                Check(false, $"{part} fits at {cell} ({why})");
            return placed?.Id ?? 0;
        }
        _chest = Place(Chest, new GridCell(0, 1, 0));
        Place(Deck, new GridCell(3, 1, 1));
        Place(Deck, new GridCell(4, 1, 1));
        Place(Pipe, new GridCell(3, 2, 1), AlongX);
        Place(Pipe, new GridCell(4, 2, 1), AlongX);
        // Turned half round, it runs from its origin toward -x and -z: x 5 to 7, z 0 to 2, its port at x 5, z 1.
        _hive = Place(Hive, new GridCell(7, 1, 2), HalfTurn);
        // two rows of four
        for (var i = 0; i < Pots.Length; i++)
        {
            var x0 = -2 + Pots.Skip(i / 4 * 4).Take(i % 4).Sum(p => PartWidth(p) + 1);
            Place(Pots[i], new GridCell(x0, 1, i < 4 ? 7 : 11));
        }
        _base = creation.Id;
        _parts = creation.Parts.Count();
        var feet = _host.Main.PlayerSync.Players[Peer].GlobalPosition;
        creation.SetAllBodyPoses(new Pose((feet + new Vector3(-1f, 0.15f, -6f)).ToNumerics(), System.Numerics.Quaternion.Identity));
        build.Added(creation);
    }

    private int PartWidth(string part) => _host.Main.Build.Catalog.FindDefinition(part)!.Size!.Value.X;

    // A part's centre in the world as `who` shows it.
    private Vector3 PartCentre(Instance who, int part)
    {
        var node = who.Main.Creations.NodeOf(_base)!;
        var placed = node.Creation.FindPart(part)!;
        var body = node.Bodies[node.Creation.BodyIndexOf(part)];
        return body.GlobalTransform * PartGeometry.CentreMetres(node.Creation.DefinitionOf(part), placed.Cell, placed.Rotation).ToGodot();
    }

    // Just off the deck's near edge, level with a part: the client never stands in the base.
    private Vector3 Beside(int part) => new(PartCentre(_host, part).X, 0, PartCentre(_host, part).Z - 1.6f);

    private void Teleport(Vector3 at) => _host.Main.PlayerSync.HostTeleport(Peer, new Vector3(at.X, Feet().Y, at.Z));

    private Vector3 Feet() => _client.Main.LocalPlayer!.GlobalPosition;

    // The clip a part's model shows (its station readout) as `who` sees it, or null before it has one.
    private string? Clip(Instance who, int part) =>
        who.Main.Creations.NodeOf(_base)?.Displays.FirstOrDefault(d => d.Part == part)?.Clip;

    private static InventoryView? Shown(Instance who, InventoryRef where) => who.Main.Inventory.Shown.GetValueOrDefault(where);

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private void RequestView(int part) =>
        Request(InventoryActions.GetInventory, new GetInventoryArgs(InventoryRef.OfPart(_base, part)));

    private InventoryView? View(int index) =>
        _results.Count > index && _results[index] is { Ok: true, Value: { } value } ? value.Deserialize<InventoryView>(ActionJson.Options) : null;

    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
            _results.Clear();
        return tick(frame);
    });

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);
}
