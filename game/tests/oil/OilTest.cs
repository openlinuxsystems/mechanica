using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Oil and chemicals (World POI plan), host plus client, on the vanilla world: the client harvests an oil geyser for 1 to
/// 4 Crude Oil and it is gone on both machines until its regrow time comes on the host's world clock; the client wades
/// into a chemical pool, drawn on both machines, and is hurt there; and an empty bucket dipped in the pool comes up a
/// Chemical Bucket (Parts map E11; game/tests/ladder pours one into a Chemical Container).
/// </summary>
public partial class OilTest : StagedTest
{
    private const string Oil = "vanilla:crude_oil";
    private const string Chemical = "vanilla:chemical";
    private const string Bucket = "vanilla:water_bucket";
    private const string ChemicalBucket = "vanilla:chemical_bucket";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private WorldObjectPlacement _geyser = null!;
    private PoiPoolPlacement _pool = null!;
    private float _health;

    protected override string Tag => "oil";

    protected override int StageTimeoutFrames => 1500;

    private int Peer => _client.Peer;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins and is taken to the nearest oil geyser", frame =>
        {
            if (_client.Main.Survival.Own is null || _client.Main.Inventory.Own is null || _client.Main.LocalPlayer is not { } player
                || !_host.Main.PlayerSync.Players.ContainsKey(Peer))
                return false;
            if (_geyser is null)
            {
                var gen = _host.Main.Streamer.Generator;
                var feet = player.GlobalPosition.ToNumerics();
                var ponds = gen.Pois.Where(p => p.Poi.Prefab.Id == "oil_pond").OrderBy(p => System.Numerics.Vector2.Distance(p.Centre, new(feet.X, feet.Z))).First();
                _geyser = gen.ObjectsIn(ponds.Centre.X - 20, ponds.Centre.Y - 20, ponds.Centre.X + 20, ponds.Centre.Y + 20).First(o => o.Definition.Id == "oil_geyser");
                var away = (_geyser.Position - new System.Numerics.Vector3(ponds.Centre.X, _geyser.Position.Y, ponds.Centre.Y)).ToGodot().Normalized();
                _host.Main.PlayerSync.HostTeleport(Peer, _geyser.Position.ToGodot() + away * 1.8f + new Vector3(0, 1.2f, 0));
                return false;
            }
            return frame > 60 && player.IsOnFloor() && player.GlobalPosition.DistanceTo(_geyser.Position.ToGodot()) < 3f
                && _client.Main.Streamer.ChunkHolding(_geyser.Id) is not null;
        });
        Add("harvesting the geyser gives 1 to 4 Crude Oil and it is gone on both machines", frame =>
        {
            if (frame == 0)
                _client.Main.Actions.Request(SurvivalActions.Harvest, new HarvestArgs(_geyser.Id.ToString()), _results.Add);
            // The inventory and the world sync separately: wait for both.
            if (_results.Count == 0 || !_client.Main.Streamer.State.IsDestroyed(_geyser.Id) || (_results[0].Ok && _client.Main.Inventory.Own!.Count(Oil) == 0))
                return false;
            Check(_results[0].Ok, $"the harvest is allowed ({_results[0].Message})");
            var oil = _client.Main.Inventory.Own!.Count(Oil);
            Check(oil is >= 1 and <= 4, $"the client has 1 to 4 Crude Oil ({oil})");
            Check(_host.Main.Streamer.State.IsDestroyed(_geyser.Id), "the host has it gone");
            Check(_host.Main.Streamer.State.Regrowing.ContainsKey(_geyser.Id), "the host will regrow it");
            Check(!_client.Main.Streamer.ChunkHolding(_geyser.Id)!.LiveObjects.Any(o => o.Id == _geyser.Id), "the client no longer draws it");
            return true;
        });
        Add("an hour of world time later the geyser wells up again on both machines", frame =>
        {
            if (frame == 0)
                _host.Main.Scripts.Runtime.Time += 3601;
            if (_host.Main.Streamer.State.IsDestroyed(_geyser.Id) || _client.Main.Streamer.State.IsDestroyed(_geyser.Id))
                return false;
            Check(_client.Main.Streamer.ChunkHolding(_geyser.Id)!.LiveObjects.Any(o => o.Id == _geyser.Id), "the client draws it again");
            return true;
        });
        Add("the client wades into a chemical pool, drawn on both machines", frame =>
        {
            if (frame == 0)
            {
                var gen = _host.Main.Streamer.Generator;
                var feet = _client.Main.LocalPlayer!.GlobalPosition.ToNumerics();
                var poi = gen.Pois.Where(p => p.Poi.Prefab.Id == "chemical_pool").OrderBy(p => System.Numerics.Vector2.Distance(p.Centre, new(feet.X, feet.Z))).First();
                _pool = gen.PoolsAt(poi.Centre.X, poi.Centre.Y).Single();
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Bucket, 1);
                _host.Main.PlayerSync.HostTeleport(Peer, _pool.Centre.ToGodot() + new Vector3(0, 0.5f, 0));
            }
            var player = _client.Main.LocalPlayer!;
            if (frame < 60 || !player.IsOnFloor() || player.GlobalPosition.DistanceTo(_pool.Centre.ToGodot()) > 3f)
                return false;
            foreach (var who in new[] { _host, _client })
            {
                var drawn = who.Main.Streamer.Chunks.Values.Any(c => c.FindChild("Pool_chemical", false, false) is MeshInstance3D);
                Check(drawn, $"{who}: the pool's surface is drawn");
            }
            Check(player.GlobalPosition.Y < _pool.Centre.Y, $"the client stands in it ({player.GlobalPosition.Y:F2} under {_pool.Centre.Y:F2})");
            _health = _client.Main.Survival.Own!.Health;
            return true;
        });
        Add("standing in the chemicals hurts", frame =>
        {
            var health = _client.Main.Survival.Own!.Health;
            if (health > _health - 9f)
                return false;
            Check(health > 0, $"it hurts over time, not all at once ({health})");
            return true;
        });
        Add("an empty bucket dipped in the pool comes up a Chemical Bucket", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var point = _pool.Centre - new System.Numerics.Vector3(0, 0.1f, 0);
                _client.Main.Actions.Request(SurvivalActions.CollectWater, new CollectWaterArgs(new Vec3(point.X, point.Y, point.Z)), _results.Add);
            }
            if (_results.Count == 0 || _client.Main.Inventory.Own!.Count(ChemicalBucket) == 0)
                return _results.Count > 0 && !_results[0].Ok && Check(false, $"the bucket fills ({_results[0].Message})");
            Check(_client.Main.Inventory.Own!.Count(ChemicalBucket) == 1, "one Chemical Bucket");
            Check(_client.Main.Inventory.Own!.Count(Bucket) == 0 && _client.Main.Inventory.Own!.Count(Chemical) == 0, "the bucket itself was filled");
            return true;
        });
    }
}
