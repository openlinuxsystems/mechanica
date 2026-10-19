using Godot;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Mechanica.Game.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Tree Harvesting spec, host plus a client, on the vanilla world. A Birch near the spawn draws its full model close up
/// and its far version past the LOD distance. The client's sledge knocks it: both peers shake it and throw chips and
/// leaves while it stands. The blow that fells it leaves 2 to 3 Birch Logs stacked up the trunk on both peers, which
/// topple away from the player as the trunk did. Sledging one of the logs breaks it into Wood Rods for both peers.
/// </summary>
public partial class ForestryTest : StagedTest
{
    private const string BirchLog = "vanilla:birch_log";
    private const string Rod = "vanilla:wood_rod";

    private readonly List<Core.Actions.ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private WorldObjectPlacement _tree = null!;
    private Vector3 _stand;
    private List<int> _logs = [];
    private int _log;

    protected override string Tag => "forestry";

    protected override int StageTimeoutFrames => 1200;

    private WorldStreamer HostWorld => _host.Main.Streamer;

    private WorldStreamer ClientWorld => _client.Main.Streamer;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", Seed = 20261017 };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Wait("players settle", 30);
        Add("find a Birch on open, gentle ground", _ =>
        {
            var gen = HostWorld.Generator;
            _tree = gen.ObjectsIn(-1000, -1000, 1000, 1000)
                .Where(o => o.Definition.Id == "birch_tree" && !gen.PoisAt(o.Position.X, o.Position.Z).Any())
                // Near level all round and where it falls (north), so the logs settle where they land instead of rolling down a hill.
                .Where(o => new[] { (0f, 0f), (7f, 0f), (-7f, 0f), (0f, 7f), (0f, -7f), (0f, -12f), (5f, -12f), (-5f, -12f) }.All(d => gen.SlopeAt(o.Position.X + d.Item1, o.Position.Z + d.Item2) < 0.08f))
                .Where(o => !gen.ObjectsIn(o.Position.X - 9, o.Position.Z - 9, o.Position.X + 9, o.Position.Z + 9).Any(n => n.Id != o.Id))
                .OrderBy(o => o.Position.Length())
                .First();
            Check(_tree.Definition.Fell is not null && _tree.Definition.BreaksInto is { Count: 2, CountMax: 3 }, $"birch {_tree.Id} at {_tree.Position} falls into 2 to 3 logs");
            // Stand 2 m south of the trunk: it must fall north, away from the player.
            var at = _tree.Position.ToGodot();
            _stand = new Vector3(at.X, gen.HeightAt(at.X, at.Z + 2f) + 0.3f, at.Z + 2f);
            return true;
        });
        Add("the client's player walks up to the tree", frame =>
        {
            if (frame == 0)
            {
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _stand, 0f);
                return false;
            }
            var chunk = ClientWorld.ChunkHolding(_tree.Id);
            return frame > 90 && chunk is not null && HostWorld.ChunkHolding(_tree.Id) is not null;
        });
        Add("the tree draws its full model close up and its far version beyond", _ =>
        {
            foreach (var world in new[] { HostWorld, ClientWorld })
            {
                var objects = world.ChunkHolding(_tree.Id)!.Objects;
                var full = objects.GetNodeOrNull<MultiMeshInstance3D>("birch_tree_mesh");
                var far = objects.GetNodeOrNull<MultiMeshInstance3D>("birch_tree_lod");
                var lod = _tree.Definition.Mesh!.LodDistance;
                Check(full is not null && Mathf.IsEqualApprox(full.VisibilityRangeEnd, lod), $"the full Birch gives way at {lod} m ({full?.VisibilityRangeEnd})");
                Check(far is not null && Mathf.IsEqualApprox(far.VisibilityRangeBegin, lod) && far.Multimesh.InstanceCount == full!.Multimesh.InstanceCount,
                    "its far version takes over there, one for each tree");
                Check(far!.Multimesh.Mesh.GetFaces().Length < full!.Multimesh.Mesh.GetFaces().Length / 4, "the far version is a fraction of the triangles");
            }
            return true;
        });
        Add("a blow shakes the tree and throws chips and leaves on both peers", frame =>
        {
            if (frame == 0)
            {
                Swing(new SledgeArgs(Object: _tree.Id.ToString(), At: Trunk()));
                return false;
            }
            if (HostWorld.Effects.Hits == 0 || ClientWorld.Effects.Hits == 0)
                return frame > 300 ? Fail($"the blow never showed ({string.Join("; ", _results)})") : false;
            Check(_results.All(r => r.Ok), $"the swing lands ({string.Join("; ", _results)})");
            Check(!HostWorld.State.IsDestroyed(_tree.Id), "one blow does not fell it");
            foreach (var world in new[] { HostWorld, ClientWorld })
            {
                Check(world.ChunkHolding(_tree.Id)!.Shaking == 1, $"{Who(world)}: the tree shakes");
                Check(world.Effects.Bursts == 2 && world.Effects.GetChildren().OfType<CpuParticles3D>().Count() == 2, $"{Who(world)}: chips fly and leaves fall");
            }
            return true;
        });
        Add("the shaking settles", frame => frame > 10 && HostWorld.ChunkHolding(_tree.Id)!.Shaking == 0 && ClientWorld.ChunkHolding(_tree.Id)!.Shaking == 0);
        Add("the felled tree falls as Birch Logs on both peers", frame =>
        {
            // No faster than one swing per sledge.swing_seconds (0.6 s): a swing every 1.25 s.
            if (frame % 75 == 40 && !HostWorld.State.IsDestroyed(_tree.Id))
                Swing(new SledgeArgs(Object: _tree.Id.ToString(), At: Trunk()));
            if (!HostWorld.State.IsDestroyed(_tree.Id) || !ClientWorld.State.IsDestroyed(_tree.Id))
                return frame > 1000 ? Fail($"the tree never fell ({string.Join("; ", _results)})") : false;
            _logs = Loose(_host, BirchLog);
            if (Loose(_client, BirchLog).Count != _logs.Count)
                return false;
            Check(_logs.Count is >= 2 and <= 3, $"it falls into 2 to 3 logs ({_logs.Count})");
            var heights = _logs.Select(id => Body(_host, id).GlobalPosition.Y).Order().ToList();
            Check(heights[^1] - heights[0] > 1.2f, $"stacked up the trunk ({string.Join(", ", heights.Select(h => h.ToString("F2")))})");
            Check(ClientWorld.Effects.Bursts >= 4, "the client sees the canopy's leaves come down");
            return true;
        });
        Add("the logs topple away from the player", frame =>
        {
            if (frame < 240 && _logs.Any(id => Body(_host, id).Freeze is false && !Body(_host, id).Sleeping))
                return false;
            var trunk = _tree.Position.ToGodot();
            foreach (var id in _logs)
            {
                var body = Body(_host, id);
                var lean = body.GlobalBasis.Z.Normalized();
                Check(Mathf.Abs(lean.Y) < 0.7f, $"log {id} is down, no longer upright (its long side {lean})");
                Check(body.GlobalPosition.Z < trunk.Z + 0.5f, $"log {id} fell north, away from the player ({body.GlobalPosition} vs trunk {trunk})");
                var client = Body(_client, id).GlobalPosition;
                Check(client.DistanceTo(body.GlobalPosition) < 0.3f, $"log {id} lies in the same place on the client ({client} vs {body.GlobalPosition})");
            }
            _log = _logs.OrderBy(id => Body(_host, id).GlobalPosition.DistanceTo(trunk)).First();
            return true;
        });
        Add("sledging a log breaks it into Wood Rods for both peers", frame =>
        {
            var hostLog = _host.Main.Creations.Find(_log);
            if (frame == 0)
            {
                _results.Clear();
                var at = Body(_host, _log).GlobalPosition;
                _host.Main.PlayerSync.HostTeleport(_client.Peer, new Vector3(at.X, HostWorld.Generator.HeightAt(at.X, at.Z + 1.2f) + 0.3f, at.Z + 1.2f), 0f);
                return false;
            }
            if (frame >= 75 && frame % 75 == 0 && hostLog is not null)
            {
                var at = Body(_host, _log).GlobalPosition;
                Swing(new SledgeArgs(Creation: _log, Part: hostLog.Parts.First().Id, At: new Core.Data.Vec3(at.X, at.Y, at.Z)));
            }
            if (hostLog is not null || _client.Main.Creations.Find(_log) is not null)
                return frame > 1000 ? Fail($"the log never broke ({string.Join("; ", _results)})") : false;
            var rods = Loose(_host, Rod);
            if (Loose(_client, Rod).Count != rods.Count || rods.Count == 0)
                return false;
            Check(rods.Count == 2, $"the log breaks into 2 Wood Rods ({rods.Count})");
            Check(_results.All(r => r.Ok), $"every swing lands ({string.Join("; ", _results)})");
            return true;
        });
    }

    private void Swing(SledgeArgs args) => _client.Main.Actions.Request(ToolActions.Sledge, args, _results.Add);

    // A point on the trunk at chest height.
    private Core.Data.Vec3 Trunk() => new(_tree.Position.X, _tree.Position.Y + 1.2f, _tree.Position.Z);

    private static List<int> Loose(Instance instance, string part) =>
        [.. instance.Main.Creations.All.Where(c => c.Parts.Count == 1 && Identifiers.Qualify(c.Parts.First().Part) == part).Select(c => c.Id)];

    private static CreationBody Body(Instance instance, int creation) => instance.Main.Creations.NodeOf(creation)!.Bodies[0];

    private string Who(WorldStreamer world) => world == HostWorld ? "host" : "client";

    private bool Fail(string message)
    {
        Check(false, message);
        return true;
    }
}
