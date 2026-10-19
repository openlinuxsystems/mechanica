using Godot;
using Mechanica.Core.Building;
using Mechanica.Core.Player;
using Mechanica.Core.Settings;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Mechanica.Game.Settings;
using Mechanica.Game.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Grass, ground cover and bushes, host plus client. Both peers grow the same flora round the same spot from the seed,
/// standing on the ground of their chunks; the Foliage density levels change how much of each kind is drawn and Off
/// removes a kind without touching the others. The seed-dropping bush is walk-through: the client's player walks
/// straight through one, ordinary rays pass it, and only the sledge's aim (which asks for walk-through objects) lands
/// on it and breaks it for both peers. When scenery turns into loaded ground, its chunks keep drawing their plants until
/// the loaded chunks' plants replace them.
/// </summary>
public partial class FloraTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private readonly Dictionary<FloraKind, int> _high = [];
    private Vector3 _spot;
    private Vector3 _far;
    private int _farDrawn;
    private List<ChunkCoord> _farChunks = [];
    private readonly Dictionary<ChunkCoord, int> _drawnAt = [];
    private WorldObjectPlacement _bush = null!;
    private Vector3 _start;
    private float _yaw;
    private readonly List<Core.Actions.ActionResult> _results = [];

    protected override string Tag => "flora";

    protected override int StageTimeoutFrames => 1200;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", Seed = 20261017 };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Wait("players settle", 30);
        Add("both players go to a meadow", frame =>
        {
            if (frame == 0)
            {
                var gen = _host.Main.Streamer.Generator;
                GD.Print($"[flora] the spawn is in {gen.BiomeAt(0, 4).Id}");
                for (var r = 100f; r < 2000f && _spot == default; r += 50f)
                {
                    for (var a = 0f; a < Mathf.Tau && _spot == default; a += 0.3f)
                    {
                        var (x, z) = (r * Mathf.Cos(a), r * Mathf.Sin(a));
                        if (gen.BiomeAt(x, z).Id == "meadow" && gen.BiomeAt(x + 60, z).Id == "meadow" && gen.BiomeAt(x - 60, z).Id == "meadow"
                            && gen.BiomeAt(x, z + 60).Id == "meadow" && gen.BiomeAt(x, z - 60).Id == "meadow" && gen.SlopeAt(x, z) < 0.3f && !gen.PoisAt(x, z).Any())
                            _spot = new Vector3(x, gen.HeightAt(x, z) + 0.5f, z);
                    }
                }
                Check(_spot != default, $"a meadow is found ({_spot})");
                _host.Main.PlayerSync.HostTeleport(_host.Peer, _spot);
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _spot + new Vector3(2, 0, 0));
                return false;
            }
            return frame > 90 && _client.Main.Streamer.Streamer.IsLoaded(_client.Main.Streamer.ChunkAt(_spot)) && _client.Main.Streamer.PendingChunks == 0
                && _host.Main.Streamer.PendingChunks == 0;
        });
        Add("both peers grow the same flora round the spot", _ =>
        {
            foreach (var instance in new[] { _host, _client })
            {
                instance.Main.Flora.Draw = true;
                instance.Main.Flora.Viewer = () => _spot;
                instance.Main.Flora.BuildAll();
            }
            foreach (var kind in new[] { FloraKind.Grass, FloraKind.GroundCover, FloraKind.Bush })
            {
                _high[kind] = _host.Main.Flora.Drawn(kind);
                Check(_high[kind] > 0, $"the host draws {kind} ({_high[kind]})");
                Check(_client.Main.Flora.Drawn(kind) == _high[kind], $"the client draws the same {kind} ({_client.Main.Flora.Drawn(kind)})");
            }
            Check(_high[FloraKind.Grass] > 5000, $"grass is dense ({_high[FloraKind.Grass]} tufts)");
            return true;
        });
        Add("each patch draws its plants with the flora shader, near, far and thinned meshes", _ =>
        {
            foreach (var (coord, node) in _host.Main.Flora.Patches)
            {
                var drawn = 0;
                foreach (var kind in node.GetChildren())
                {
                    var meshes = kind.GetChildren().OfType<MultiMeshInstance3D>().ToList();
                    var near = meshes.Where(m => m.Name.ToString().EndsWith("_near", StringComparison.Ordinal)).ToList();
                    var far = meshes.Where(m => m.Name.ToString().EndsWith("_far", StringComparison.Ordinal)).ToList();
                    Check(near.Count > 0 && near.Count == far.Count, $"{coord} {kind.Name}: a near and a far mesh per model ({near.Count}, {far.Count})");
                    Check(meshes.All(m => m.MaterialOverride is ShaderMaterial), $"{coord} {kind.Name}: every mesh uses the flora shader");
                    Check(near.Zip(far).All(p => p.First.VisibilityRangeEnd == p.Second.VisibilityRangeBegin && p.Second.VisibilityRangeEnd > p.Second.VisibilityRangeBegin),
                        $"{coord} {kind.Name}: the far mesh takes over where the near one ends");
                    Check(near.Zip(far).All(p => p.First.Multimesh.InstanceCount == p.Second.Multimesh.InstanceCount), $"{coord} {kind.Name}: both draw the same plants");
                    Check(meshes.All(m => m.CastShadow == (kind.Name == "Bush" ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off)),
                        $"{coord} {kind.Name}: only bushes cast shadows");
                    // Past the thin distance grass and ground cover draw a few of their plants, larger, out to the end.
                    var thin = meshes.Where(m => m.Name.ToString().EndsWith("_thin", StringComparison.Ordinal)).ToList();
                    Check(kind.Name == "Bush" ? thin.Count == 0 : thin.Count > 0, $"{coord} {kind.Name}: thinned far meshes only for grass and ground cover ({thin.Count})");
                    foreach (var mesh in thin)
                    {
                        var model = mesh.Name.ToString()[..^"_thin".Length];
                        var full = far.Single(m => m.Name == $"{model}_far");
                        Check(mesh.VisibilityRangeBegin == full.VisibilityRangeEnd && mesh.VisibilityRangeEnd > mesh.VisibilityRangeBegin,
                            $"{coord} {mesh.Name}: takes over where the far mesh ends");
                        Check(mesh.Multimesh.InstanceCount < full.Multimesh.InstanceCount || full.Multimesh.InstanceCount < 4,
                            $"{coord} {mesh.Name}: draws fewer plants ({mesh.Multimesh.InstanceCount} of {full.Multimesh.InstanceCount})");
                    }
                    drawn += near.Sum(m => m.Multimesh.InstanceCount);
                }
                Check(drawn > 0 || node.GetChildCount() == 0, $"{coord}: a patch with meshes draws plants ({drawn})");
            }
            Check(_host.Main.Flora.Patches.Sum(p => p.Node.GetChildren().Sum(k => k.GetChildren().OfType<MultiMeshInstance3D>().Where(m => m.Name.ToString().EndsWith("_near", StringComparison.Ordinal)).Sum(m => m.Multimesh.InstanceCount)))
                == _high.Values.Sum(), "the meshes draw every plant counted");
            return true;
        });
        Add("the ground is painted with the world's ground textures", _ =>
        {
            foreach (var peer in new[] { _host, _client })
            {
                var chunk = peer.Main.Streamer.Chunks.Values.First();
                var mesh = chunk.GetNode<MeshInstance3D>("Mesh");
                Check(mesh.MaterialOverride == GroundMaterial.For(peer.Main.Streamer.Generator.Data) && mesh.MaterialOverride is ShaderMaterial,
                    $"{chunk.Name}: drawn with the ground texture material");
                var format = ((ArrayMesh)mesh.Mesh).SurfaceGetFormat(0);
                Check((format & Mesh.ArrayFormat.FormatCustom0) != 0 && (format & Mesh.ArrayFormat.FormatCustom1) != 0, $"{chunk.Name}: carries ground weights");
                var layers = (Texture2DArray)((ShaderMaterial)mesh.MaterialOverride).GetShaderParameter("grounds");
                Check(layers.GetLayers() == Mechanica.Core.World.TerrainChunkData.GroundLayers, $"{layers.GetLayers()} texture layers");
            }
            return true;
        });
        Add("foliage density levels change how much is drawn", _ =>
        {
            var flora = _host.Main.Flora;
            int Drawn(FoliageLevel grass, FoliageLevel cover, FoliageLevel bushes, FloraKind kind)
            {
                GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = grass, GroundCover = cover, Bushes = bushes } });
                flora.BuildAll();
                return flora.Drawn(kind);
            }
            const FoliageLevel high = FoliageLevel.High;
            var low = Drawn(FoliageLevel.Low, high, high, FloraKind.Grass);
            Check(low > 0 && low < _high[FloraKind.Grass] * 0.35f, $"Low draws far less grass ({low} of {_high[FloraKind.Grass]})");
            var medium = Drawn(FoliageLevel.Medium, high, high, FloraKind.Grass);
            Check(medium > low && medium < _high[FloraKind.Grass], $"Medium sits between ({medium})");
            var veryHigh = Drawn(FoliageLevel.VeryHigh, high, high, FloraKind.Grass);
            Check(veryHigh > _high[FloraKind.Grass] * 1.5f, $"Very high draws more ({veryHigh})");
            Check(Drawn(FoliageLevel.Off, high, high, FloraKind.Grass) == 0, "Off draws no grass");
            Check(flora.Drawn(FloraKind.Bush) == _high[FloraKind.Bush] && flora.Drawn(FloraKind.GroundCover) == _high[FloraKind.GroundCover], "turning grass off leaves the bushes and ground cover alone");
            Check(Drawn(high, high, FoliageLevel.Off, FloraKind.Bush) == 0, "Off draws no bushes");
            Check(flora.Drawn(FloraKind.Grass) == _high[FloraKind.Grass], "and grass comes back as it was");
            Check(Drawn(high, FoliageLevel.Off, high, FloraKind.GroundCover) == 0, "Off draws no ground cover");
            Drawn(high, high, high, FloraKind.Grass);
            return true;
        });

        Add("plants stay drawn while scenery turns into loaded ground", frame =>
        {
            var streamer = _host.Main.Streamer;
            var flora = _host.Main.Flora;
            if (frame == 0)
            {
                streamer.DrawScenery = true;
                return false;
            }
            if (_far == default)
            {
                if ((streamer.SceneryMissing > 0 || streamer.Scenery.Count == 0 || frame < 10) && frame < 600)
                    return false;
                // A spot 250 m off, drawn only as scenery, with grass round it.
                for (var a = 0f; a < Mathf.Tau && _far == default; a += Mathf.Pi / 4f)
                {
                    var at = _spot + new Vector3(250f * Mathf.Cos(a), 0f, 250f * Mathf.Sin(a));
                    flora.Viewer = () => at;
                    flora.BuildAll();
                    var near = Near(at);
                    if (near.All(c => streamer.Scenery.ContainsKey(c)) && near.Any(c => _drawnAt.GetValueOrDefault(c) > 0))
                        (_far, _farChunks) = (at, near.Where(c => _drawnAt.GetValueOrDefault(c) > 0).ToList());
                }
                Check(_far != default, $"a grassy scenery spot is found ({_far})");
                _farDrawn = flora.Drawn(FloraKind.Grass);
                var ground = streamer.Generator.HeightAt(_far.X, _far.Z);
                _host.Main.PlayerSync.HostTeleport(_client.Peer, new Vector3(_far.X, Mathf.Max(ground, 0f) + 1f, _far.Z));
                return _far == default;
            }
            // Every frame while the scenery gives way to loaded chunks, each patch round the spot still draws its plants.
            var patches = flora.Patches.ToDictionary(p => p.Chunk, p => p.Node);
            foreach (var coord in _farChunks)
            {
                if (!patches.TryGetValue(coord, out var node) || node.GetChildCount() == 0)
                    return Fail($"{coord} lost its plants at frame {frame} (scenery {streamer.Scenery.ContainsKey(coord)}, loaded {streamer.Chunks.ContainsKey(coord)}, pending {streamer.IsPending(coord)})");
            }
            if (!_farChunks.All(streamer.Chunks.ContainsKey) || streamer.PendingChunks > 0)
                return frame > 1100 ? Fail("the chunks round the spot never loaded") : false;
            flora.BuildAll();
            Check(flora.Drawn(FloraKind.Grass) == _farDrawn, $"the loaded chunks grow the same grass ({flora.Drawn(FloraKind.Grass)} of {_farDrawn})");
            streamer.DrawScenery = false;
            flora.Viewer = () => _spot;
            return true;
        });

        Add("find a seed bush on open ground", _ =>
        {
            var gen = _host.Main.Streamer.Generator;
            // Near the spawn, where the world's chunks are already in.
            var home = gen.Spawn ?? default;
            _bush = gen.ObjectsIn(home.X - 300, home.Z - 300, home.X + 300, home.Z + 300)
                .Where(o => o.Definition.Id == "bush" && gen.SlopeAt(o.Position.X, o.Position.Z) < 0.2f)
                .Where(o => !gen.ObjectsIn(o.Position.X - 8, o.Position.Z - 8, o.Position.X + 8, o.Position.Z + 8).Any(n => n.Id != o.Id))
                .OrderBy(o => (o.Position - home).Length())
                .First();
            Check(!_bush.Definition.Solid && _bush.Definition.Model == "bush_berry", $"bush {_bush.Id} is a walk-through berry bush at {_bush.Position}");
            // Start 3 m short of it, facing across it.
            var at = _bush.Position.ToGodot();
            var dir = new Vector2(1, 0.3f).Normalized();
            _start = at - new Vector3(dir.X, 0, dir.Y) * 3f;
            _start.Y = gen.HeightAt(_start.X, _start.Z) + 0.3f;
            _yaw = Mathf.Atan2(-dir.X, -dir.Y);
            return true;
        });
        Add("the client's player walks straight through the bush", frame =>
        {
            var onHost = _host.Main.PlayerSync.Players[_client.Peer];
            var mine = _client.Main.LocalPlayer!;
            if (frame == 0)
            {
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _start, _yaw);
                return false;
            }
            if (frame < 60)
            {
                mine.CurrentInput = PlayerInput.None with { Yaw = _yaw };
                return false;
            }
            var past = Along(onHost.GlobalPosition) > 1.5f;
            mine.CurrentInput = past ? PlayerInput.None with { Yaw = _yaw } : new PlayerInput(0, 1, _yaw, 0, false, false);
            if (!past && frame < 400)
                return false;
            Check(past, $"the player got past the bush ({Along(onHost.GlobalPosition):F2} m beyond its centre)");
            var side = Flat(onHost.GlobalPosition - _bush.Position.ToGodot()).Dot(new Vector2(-0.3f, 1).Normalized());
            Check(Mathf.Abs(side) < 0.5f, $"without being pushed aside ({side:F2} m off line)");
            return true;
        });
        Add("rays pass the bush but the sledge's aim hits it", frame =>
        {
            var mine = _client.Main.LocalPlayer!;
            if (frame == 0)
            {
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _start, _yaw);
                return false;
            }
            // Stand 1.6 m short of the bush, looking down at the middle of its collider (bushes vary in size).
            var middle = _bush.Position.ToGodot() + new Vector3(0, _bush.Definition.Collider!.At.Y * _bush.Scale, 0);
            var pitch = frame < 60 ? -0.45f : Mathf.Atan2(middle.Y - mine.AimOrigin.Y, Flat(middle - mine.AimOrigin).Length());
            mine.CurrentInput = PlayerInput.None with { Yaw = _yaw, Pitch = pitch };
            if (frame < 60)
                return false;
            if (frame == 60)
            {
                var toward = Flat(_bush.Position.ToGodot() - mine.GlobalPosition);
                _host.Main.PlayerSync.HostTeleport(_client.Peer, mine.GlobalPosition + new Vector3(toward.X, 0, toward.Y).Normalized() * (toward.Length() - 1.6f), _yaw);
                return false;
            }
            if (frame < 120)
                return false;
            var plain = HandTool.Cast(mine, 4f);
            Check(plain is null || TerrainChunk.ObjectHit(plain.Value.Collider, plain.Value.Shape) is null, "a plain ray goes through the bush to the ground");
            var aim = HandTool.Cast(mine, 4f, brush: true);
            Check(aim is { } hit && TerrainChunk.ObjectHit(hit.Collider, hit.Shape) == _bush.Id, $"the sledge's aim lands on bush {_bush.Id}");
            foreach (var instance in new[] { _host, _client })
            {
                var top = _bush.Position.ToGodot() + new Vector3(0, 3, 0);
                var query = PhysicsRayQueryParameters3D.Create(top, top - new Vector3(0, 5, 0));
                var hitBody = instance.Main.GetWorld3D().DirectSpaceState.IntersectRay(query);
                Check(hitBody.Count > 0 && TerrainChunk.ObjectHit(hitBody["collider"].AsGodotObject(), hitBody["shape"].AsInt32()) is null, $"{instance}: a ray down passes the bush");
                var shape = new PhysicsShapeQueryParameters3D
                {
                    Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f },
                    Transform = new Transform3D(Basis.Identity, _bush.Position.ToGodot() + new Vector3(0, 1f, 0)),
                };
                var space = instance.Main.GetWorld3D().DirectSpaceState;
                bool HitsBush(Godot.Collections.Array<Godot.Collections.Dictionary> hits) =>
                    hits.Any(h => TerrainChunk.ObjectHit(h["collider"].AsGodotObject(), h["shape"].AsInt32()) == _bush.Id);
                Check(!HitsBush(space.IntersectShape(shape, 8)), $"{instance}: a body standing in the bush touches nothing of it");
                shape.CollideWithAreas = true;
                Check(HitsBush(space.IntersectShape(shape, 8)), $"{instance}: its walk-through collider is there");
            }
            return true;
        });
        Add("sledging the bush breaks it for both peers", frame =>
        {
            if (frame % 30 == 0 && !_client.Main.Streamer.State.IsDestroyed(_bush.Id))
                _client.Main.Actions.Request(ToolActions.Sledge, new SledgeArgs(Object: _bush.Id.ToString(), At: new Core.Data.Vec3(_bush.Position.X, _bush.Position.Y, _bush.Position.Z)), _results.Add);
            if (!_host.Main.Streamer.State.IsDestroyed(_bush.Id) || !_client.Main.Streamer.State.IsDestroyed(_bush.Id))
                return frame > 900 ? Fail($"the bush never broke ({string.Join("; ", _results)})") : false;
            Check(_results.All(r => r.Ok), $"every swing lands ({string.Join("; ", _results)})");
            return true;
        });
    }

    private bool Fail(string message)
    {
        Check(false, message);
        return true;
    }

    // The chunks within 40 m of a spot, recording how much grass the host draws in each.
    private List<ChunkCoord> Near(Vector3 at)
    {
        var streamer = _host.Main.Streamer;
        var near = new List<ChunkCoord>();
        for (var dx = -40f; dx <= 40f; dx += 40f)
        {
            for (var dz = -40f; dz <= 40f; dz += 40f)
            {
                var coord = streamer.ChunkAt(at + new Vector3(dx, 0, dz));
                if (!near.Contains(coord))
                    near.Add(coord);
            }
        }
        _drawnAt.Clear();
        foreach (var (coord, node) in _host.Main.Flora.Patches)
            _drawnAt[coord] = node.GetNodeOrNull<Node3D>("Grass") is { } grass ? grass.GetChildCount() : 0;
        return near;
    }

    // Metres past the bush's centre along the walk.
    private float Along(Vector3 position) => Flat(position - _bush.Position.ToGodot()).Dot(new Vector2(1, 0.3f).Normalized());

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);
}
