using Godot;
using Mechanica.Core.Player;
using Mechanica.Core.World;
using Mechanica.Game.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Generated terrain test (chunk 19), host plus clients over localhost ENet, on the vanilla world rules.
/// The client must regenerate the host's world from the host's seed: the same heights, height-map
/// collision, crash site, pond and markers. The client's player then walks onto a hillside, where both
/// peers must hold it on the ground. A tree there must be a collider the host can identify; the host
/// destroys it, the client must lose it too, and a client joining afterwards must never see it.
/// </summary>
public partial class TerrainTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Instance? _late;
    private Vector3 _hillside;
    private WorldObjectPlacement _tree = null!;

    protected override string Tag => "terrain";

    private WorldStreamer HostWorld => _host.Main.Streamer;

    private WorldStreamer ClientWorld => _client.Main.Streamer;

    // The start: the spawn, wreck and pond are laid out round it.
    private PoiInstance Crash => HostWorld.Generator.Pois.First(p => p.Poi.Prefab.Id == "crash_site");

    private Vector3 Pond => Crash.ToWorld(new System.Numerics.Vector3(-56, 0, -3)).ToGodot();

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", Seed = 20261017 };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Wait("players settle", 30);
        Add("the client regenerates the host's world", _ =>
        {
            Check(HostWorld.WorldGen.Pois.Count >= 50 && !HostWorld.WorldGen.HasErrors, "the vanilla world rules and POIs load");
            Check(ClientWorld.State.Seed == 20261017 && ClientWorld.Generator.Seed == HostWorld.Generator.Seed, "the client uses the host's seed");
            Check(ClientWorld.Generator.Pois.Select(p => p.Centre).SequenceEqual(HostWorld.Generator.Pois.Select(p => p.Centre)), "both peers lay out the same POIs");
            var home = HostWorld.ChunkAt(Pond);
            var (host, client) = (HostWorld.Chunks[home], ClientWorld.Chunks[home]);
            Check(host.Data.Heights.SequenceEqual(client.Data.Heights), $"chunk {home} at the start has the same heights on both peers");
            var shape = host.GroundShape.Shape as HeightMapShape3D;
            Check(shape is not null && shape.MapData.SequenceEqual(host.Data.Heights), "the ground collides as a height map of those heights");
            var spawn = Crash.ToWorld(new System.Numerics.Vector3(16, 0, 36)).ToGodot();
            Check(_host.Main.GetNode<Node3D>("SpawnPoint").GlobalPosition.IsEqualApprox(spawn), $"players spawn at the crash site's spawn marker ({spawn})");
            foreach (var instance in new[] { _host, _client })
            {
                var y = instance.Main.LocalPlayer!.GlobalPosition.Y - Crash.PadHeight;
                Check(y is > -0.05f and < 0.1f, $"{instance} player stands on the crash site pad ({y:F3} m over it)");
            }
            return true;
        });
        Add("the crash site has its wreck, fires, battery and pond", _ =>
        {
            foreach (var world in new[] { HostWorld, ClientWorld })
            {
                var who = world == HostWorld ? "host" : "client";
                var markers = world.Chunks.Values.SelectMany(c => c.Markers.GetChildren().OfType<Marker3D>()).ToList();
                Check(markers.Count(m => (string)m.GetMeta("type") == "fire") == 3, $"{who}: three fire markers");
                Check(markers.Any(m => (string)m.GetMeta("ref") == "master_battery"), $"{who}: a master battery marker");
                var wreck = world.Chunks.Values.Sum(c => c.Structures.GetChildren().OfType<CollisionShape3D>().Count());
                Check(wreck >= 6, $"{who}: the wreck's pieces collide ({wreck})");
                var pond = world.Chunks[world.ChunkAt(Pond)];
                Check(pond.Data.HasWater && pond.GetNodeOrNull("Water") is not null, $"{who}: the pond chunk shows water");
            }
            return true;
        });

        Add("a player who falls into the pond swims out", frame =>
        {
            // Floating in the middle of the crash site's pond, the player heads for the spawn holding forward
            // and jump. The banks must let them climb out rather than trap them in the water.
            var onHost = _host.Main.PlayerSync.Players[_client.Peer];
            var mine = _client.Main.LocalPlayer!;
            var sea = HostWorld.Generator.SeaLevel;
            var spawn = Crash.ToWorld(new System.Numerics.Vector3(16, 0, 36)).ToGodot();
            var toSpawn = new Vector2(spawn.X - Pond.X, spawn.Z - Pond.Z).Normalized();
            var yaw = MathF.Atan2(-toSpawn.X, -toSpawn.Y);
            if (frame == 0)
            {
                onHost.ApplyState(new Vector3(Pond.X, sea - onHost.Settings.FloatDepth, Pond.Z), Vector3.Zero, yaw, 0f);
                return false;
            }
            if (frame < 60)
            {
                mine.CurrentInput = PlayerInput.None with { Yaw = yaw };
                return false;
            }
            if (frame == 60)
                Check(PlayerMotor.IsSwimming(onHost.WaterDepth, onHost.Settings), $"the player floats in the pond (feet {onHost.WaterDepth:F2} m under)");
            var outOfWater = frame > 60 && onHost.IsOnFloor() && onHost.GlobalPosition.Y > sea + 0.3f;
            mine.CurrentInput = outOfWater ? PlayerInput.None with { Yaw = yaw } : new PlayerInput(0, 1, yaw, 0, true, false);
            if (!outOfWater)
                return false;
            Check(onHost.WaterDepth < 0f, $"the player stands on dry ground (y={onHost.GlobalPosition.Y:F2}, sea {sea})");
            return true;
        });

        Add("find a wooded hillside", _ =>
        {
            var gen = HostWorld.Generator;
            var home = Crash.Centre;
            for (var z = home.Y - 1500f; z < home.Y + 1500f && _tree is null; z += 23f)
            {
                for (var x = home.X - 1500f; x < home.X + 1500f && _tree is null; x += 23f)
                {
                    if (!gen.InWorld(x, z))
                        continue;
                    var slope = gen.SlopeAt(x, z);
                    if (slope is < 0.12f or > 0.5f || gen.HeightAt(x, z) < gen.SeaLevel + 1f || gen.Pois.Any(p => System.Numerics.Vector2.Distance(p.Centre, new(x, z)) < p.Radius + 20f))
                        continue;
                    var near = gen.ObjectsIn(x - 30, z - 30, x + 30, z + 30).ToList();
                    if (near.Any(o => Flat(o.Position).DistanceTo(new Vector2(x, z)) < 4f))
                        continue;
                    if (near.FirstOrDefault(o => o.Definition.Id?.EndsWith("_tree") == true && Flat(o.Position).DistanceTo(new Vector2(x, z)) > 6f) is { } tree)
                    {
                        _hillside = new Vector3(x, gen.HeightAt(x, z), z);
                        _tree = tree;
                    }
                }
            }
            Check(_tree is not null, $"a hillside with a tree nearby exists ({_hillside}, slope {HostWorld.Generator.SlopeAt(_hillside.X, _hillside.Z):F2})");
            return true;
        });
        Add("the client's player walks onto the hillside", frame =>
        {
            var onHost = _host.Main.PlayerSync.Players[_client.Peer];
            if (frame == 0)
            {
                onHost.ApplyState(_hillside + new Vector3(0, 0.5f, 0), Vector3.Zero, 0f, 0f);
                return false;
            }
            var mine = _client.Main.LocalPlayer!;
            if (frame < 90 || mine.GlobalPosition.DistanceTo(onHost.GlobalPosition) > 0.1f)
                return false;
            var ground = HostWorld.Generator.HeightAt(onHost.GlobalPosition.X, onHost.GlobalPosition.Z);
            Check(MathF.Abs(onHost.GlobalPosition.Y - ground) < 0.3f, $"the host holds the player on the slope (y={onHost.GlobalPosition.Y:F3}, ground {ground:F3})");
            Check(onHost.Velocity.Length() < 0.5f, $"the player is at rest ({onHost.Velocity.Length():F3} m/s)");
            Check(ClientWorld.Streamer.IsLoaded(ClientWorld.ChunkAt(mine.GlobalPosition)), "the client streamed the hillside for its player");
            Check(MathF.Abs(mine.GlobalPosition.Y - ground) < 0.3f, $"the client's prediction stands there too (y={mine.GlobalPosition.Y:F3})");
            return true;
        });
        Add("the tree is a collider both peers can name", _ =>
        {
            foreach (var instance in new[] { _host, _client })
                Check(TreeHit(instance) == _tree.Id, $"{instance}: a ray down onto the tree hits object {_tree.Id}");
            return true;
        });
        Add("destroying the tree on the host removes it everywhere", frame =>
        {
            if (frame == 0)
            {
                Check(!ClientWorld.DestroyObject(_tree.Id), "a client cannot destroy world objects");
                Check(HostWorld.DestroyObject(_tree.Id), "the host destroys the tree");
                Check(!HostWorld.DestroyObject(_tree.Id), "a destroyed tree cannot be destroyed again");
                Check(TreeHit(_host) is null, "the host's ray now reaches the ground");
                return false;
            }
            if (!ClientWorld.State.IsDestroyed(_tree.Id))
                return false;
            Check(TreeHit(_client) is null, "the client's ray reaches the ground too");
            Check(ClientWorld.Chunks[ClientWorld.ChunkAt(_tree.Position.ToGodot())].LiveObjects.All(o => o.Id != _tree.Id), "the client's chunk no longer lists it");
            return true;
        });
        Add("a late joiner never sees the tree", frame =>
        {
            if (frame == 0)
            {
                _late = _harness.StartClient("Late");
                return false;
            }
            if (_late!.Main.Session.State != Net.NetSession.SessionState.Connected || _late.Main.Streamer.State.Seed != HostWorld.State.Seed)
                return false;
            Check(_late.Main.Streamer.State.IsDestroyed(_tree.Id), "the late joiner receives the destroyed tree");
            return true;
        });
    }

    // What a ray straight down onto the tree hits: its object id, or null for anything else.
    private WorldObjectId? TreeHit(Instance instance)
    {
        var top = _tree.Position.ToGodot();
        var hit = instance.Main.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(top + new Vector3(0, 30, 0), top - new Vector3(0, 2, 0)));
        if (hit.Count == 0 || hit["collider"].As<Node>() is not StaticBody3D { Name: var name } body || name != "Objects" || body.GetParent() is not TerrainChunk chunk)
            return null;
        return chunk.ObjectAt(hit["shape"].AsInt32());
    }

    private static Vector2 Flat(System.Numerics.Vector3 v) => new(v.X, v.Z);
}
