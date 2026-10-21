using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Parts map batch 7's beds and stations, host plus a client in Survival on flat ground. The host puts a Fancy Bed and a
/// Freezer beside the client. The client sleeps in the Fancy Bed (a respawn point like the Respawn Bed), dies and
/// respawns on top of it; then it crafts Ice Blocks at the Freezer from a full Water Bucket: the bucket leaves its
/// inventory, the Freezer works (its clip shows on the client) and, once the craft is done, holds 20 Ice Blocks and the
/// empty bucket, which the client sees in the Freezer's view.
/// </summary>
public partial class BedsTest : StagedTest
{
    private const string FullBucket = "vanilla:water_bucket_full";
    private const string Ice = "vanilla:ice_block";
    private const string Bucket = "vanilla:water_bucket";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _bed;
    private int _freezer;

    protected override string Tag => "beds";

    // A respawn waits for the delay (5 s).
    protected override int StageTimeoutFrames => 900;

    private SurvivalActions HostSurvival => _host.Main.Survival.Actions!;

    private int Peer => _client.Peer;

    private InventoryRef FreezerRef => InventoryRef.OfPart(_freezer, 1);

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Survival.Own is { Dead: false } && _client.Main.Inventory.Own is not null
            && _client.Main.LocalPlayer is { } player && player.IsOnFloor());
        Stage("the host puts a Fancy Bed and a Freezer beside the client", frame =>
        {
            if (frame == 0)
            {
                var feet = Feet(_client);
                Spawn("vanilla:fancy_bed", feet + new Vector3(2, 0.4f, 0), id => _bed = id);
                Spawn("vanilla:freezer", feet + new Vector3(-1.5f, 0.6f, 0), id => _freezer = id);
            }
            return frame > 30 && _bed != 0 && _freezer != 0
                && _client.Main.Creations.Find(_bed) is not null && _client.Main.Creations.Find(_freezer) is not null;
        });
        Stage("the client sleeps in the Fancy Bed", frame =>
        {
            if (frame == 0)
                Request(SurvivalActions.SetSpawn, new PartUseArgs(_bed, _host.Main.Creations.Find(_bed)!.Parts.First().Id));
            if (_results.Count == 0 || _client.Main.Survival.Own is not { HasBed: true })
                return false;
            Check(_results[0].Ok, $"the bed is set ({_results[0]})");
            return true;
        });
        Stage("the client dies", frame =>
        {
            if (frame == 0)
                HostSurvival.Damage(Peer, 500, "test");
            return _client.Main.Survival.Own is { Dead: true };
        });
        Stage("the client respawns on the Fancy Bed", frame =>
        {
            if (frame % 30 == 0 && _client.Main.Survival.Own is { Dead: true } && _client.Main.Survival.OwnRespawnIn <= 0)
                Request(SurvivalActions.Respawn, new NoArgs());
            if (_client.Main.Survival.Own is not { Dead: false })
                return false;
            var node = _host.Main.Creations.NodeOf(_bed)!;
            var part = node.Creation.Parts.First();
            var bed = node.Bodies[0].GlobalTransform * PartGeometry.CentreMetres(node.Creation.DefinitionOf(part.Id), part.Cell, part.Rotation).ToGodot();
            var feet = _host.Main.PlayerSync.Players[Peer].GlobalPosition;
            Check(new Vector2(feet.X - bed.X, feet.Z - bed.Z).Length() < 0.1f && feet.Y > bed.Y, $"respawned on top of the bed ({feet} vs its centre {bed})");
            return true;
        });

        Stage("the client opens the Freezer: a station with one recipe", frame =>
        {
            if (frame == 0)
            {
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(FullBucket, 1);
                _client.Main.Inventory.Open(FreezerRef);
            }
            if (Shown(_client, FreezerRef) is not { } view || Count(FullBucket) != 1)
                return false;
            Check(view.Part == "vanilla:freezer" && view.Queue is { Count: 0 } && view.SlotCount == 4,
                $"the Freezer's view arrives ({view.Part}, {view.SlotCount} slots)");
            Check(Clip(_client, _freezer) is null or "" or "idle_loop", $"it idles ({Clip(_client, _freezer)})");
            return true;
        });
        Stage("the client freezes a bucket of water: the bucket goes in and the Freezer works on both machines", frame =>
        {
            if (frame == 0)
                Request(InventoryActions.Craft, new CraftArgs(_freezer, 1, "vanilla:ice_block_freezer", 1));
            if (_results.Count == 0)
                return false;
            if (!Check(_results[0].Ok, $"the host queues it ({_results[0]})"))
                return true;
            if (Shown(_client, FreezerRef) is not { Queue: [var job] } || Clip(_client, _freezer)?.Contains("work_loop") != true)
                return false;
            Check(job.Recipe == "vanilla:ice_block_freezer" && job.Time == 60f, $"the order shows in its queue ({job.Recipe}, {job.Time} s)");
            Check(Count(FullBucket) == 0, "the full bucket left the client's inventory");
            Check(Clip(_host, _freezer)?.Contains("work_loop") == true, $"the host's Freezer works too ({Clip(_host, _freezer)})");
            return true;
        });
        Stage("a minute on, the Freezer holds 20 Ice Blocks and the empty bucket, on the client too", frame =>
        {
            if (frame == 0)
                _host.Main.Inventory.Items!.Tick(61f);
            if (Shown(_client, FreezerRef) is not { Queue.Count: 0 } view || view.Count(Ice) != 20)
                return false;
            Check(view.Count(Bucket) == 1, "the empty bucket came back");
            var held = _host.Main.Creations.Find(_freezer)!.InventoryOf(1)!;
            Check(held.Count(Ice) == 20 && held.Count(Bucket) == 1, "the host's Freezer holds them");
            Check(Clip(_client, _freezer)?.StartsWith("complete", StringComparison.Ordinal) == true, $"it plays its complete clip ({Clip(_client, _freezer)})");
            return true;
        });
    }

    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
            _results.Clear();
        return tick(frame);
    });

    private void Spawn(string part, Vector3 at, Action<int> created) =>
        _host.Main.Actions.Request(CreationActions.SpawnPart, new SpawnPartArgs(part, [at.X, at.Y, at.Z]), r =>
        {
            Check(r.Ok, $"{part} spawns ({r})");
            if (r.Ok)
                created(r.Value!.Value.GetInt32());
        });

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    // The clip a part's model shows on a machine (its display's clip readout), or null before it has one.
    private static string? Clip(Instance instance, int creation) =>
        instance.Main.Creations.NodeOf(creation)?.Displays.FirstOrDefault(d => d.Part == 1)?.Clip;

    private static InventoryView? Shown(Instance who, InventoryRef where) => who.Main.Inventory.Shown.GetValueOrDefault(where);

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private static Vector3 Feet(Instance who) => who.Main.LocalPlayer!.GlobalPosition;
}
