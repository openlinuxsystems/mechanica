using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.World;
using Mechanica.Game.Menus;
using Mechanica.Game.World;

namespace Mechanica.Game.Tests;

/// <summary>
/// View distance draws the world past the simulated ground (UI spec §16 Graphics). With scenery on, chunks
/// fill in out to the view distance, nearest first, never overlapping the simulated chunks and with no
/// colliders; walking leaves no hole in the ground the sky shows through, at the simulated chunks' edge or
/// between chunks; a shorter view distance drops the far ones and the shortest draws the simulated ground alone.
/// </summary>
public partial class SceneryTest : StagedTest
{
    private Main _main = null!;
    private WorldStreamer _streamer = null!;
    private int _far;
    private Vector3 _walk;
    private readonly HashSet<ChunkCoord> _seen = [];
    private readonly List<string> _holes = [];

    protected override string Tag => "scenery";

    public override void _Ready()
    {
        Add("a world starts", frame =>
        {
            if (frame == 0)
            {
                _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
                _main.Options = LaunchOptions.Offline with { Seed = 7 };
                AddChild(_main);
                return false;
            }
            if (_main.LocalPlayer is null)
                return false;
            _streamer = _main.Streamer;
            _streamer.ViewDistance = 384f;
            _streamer.DrawScenery = true;
            return true;
        });
        Add("scenery fills in out to the view distance", frame =>
        {
            var wanted = Wanted(384f);
            if (_streamer.Scenery.Count < wanted && frame < 1200)
                return false;
            Check(_streamer.Scenery.Count == wanted && wanted > 0, $"every chunk in view is drawn ({_streamer.Scenery.Count} of {wanted})");
            Check(!_streamer.Scenery.Keys.Any(_streamer.Chunks.ContainsKey), "scenery never overlaps the simulated ground");
            var viewer = _main.LocalPlayer!.GlobalPosition.ToNumerics();
            Check(_streamer.Scenery.Keys.Max(c => FarChunks.Distance(c, viewer, 64f)) > 300f, "it reaches past 300 m");
            var shapes = _streamer.Ground.GetChildren().OfType<CollisionShape3D>().Count(s => s.Shape is HeightMapShape3D);
            Check(shapes == _streamer.Chunks.Count, $"only simulated chunks have ground colliders ({shapes} for {_streamer.Chunks.Count})");
            Check(_streamer.Scenery.Values.All(c => !c.Objects.GetChildren().OfType<CollisionShape3D>().Any()), "scenery objects have no colliders");
            _far = _streamer.Scenery.Count;
            return true;
        });
        Add("every chunk's ground hangs a skirt below its edges", _ =>
        {
            foreach (var chunk in _streamer.Chunks.Values.Take(1).Concat(_streamer.Scenery.Values.Take(1)))
            {
                var mesh = (ArrayMesh)chunk.GetNode<MeshInstance3D>("Mesh").Mesh;
                var vertices = (Vector3[])mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex];
                var lowest = chunk.Data.Heights.Min();
                Check(vertices.Min(v => v.Y) < lowest - 0.4f, $"{chunk.Name}: the skirt hangs below the ground ({vertices.Min(v => v.Y):0.0} < {lowest:0.0})");
            }
            return true;
        });
        Add("walking never opens a hole in the ground in view", frame =>
        {
            var player = _main.LocalPlayer!;
            if (frame == 0)
                _walk = player.GlobalPosition;
            // Ground the player has seen drawn stays drawn while it is in view, as chunks load ahead and unload behind.
            var viewer = player.GlobalPosition.ToNumerics();
            _holes.AddRange(_seen.Where(c => FarChunks.Distance(c, viewer, 64f) < 384f - 64f && !_streamer.Draws(c)).Select(c => $"{c} at frame {frame}"));
            _seen.UnionWith(FarChunks.Wanted(viewer, 384f, 64f, _streamer.Streamer.InWorld, new HashSet<ChunkCoord>()).Where(_streamer.Draws));
            if (frame >= 160)
            {
                if (_streamer.Scenery.Count < Wanted(384f) && frame < 1400)
                    return false;
                Check(_holes.Count == 0, $"ground in view stays drawn ({_holes.Count} holes: {string.Join(", ", _holes.Take(5))})");
                return true;
            }
            _walk += new Vector3(3f, 0, 0);
            var gen = _streamer.Generator;
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, new Vector3(_walk.X, gen.HeightAt(_walk.X, _walk.Z) + 1f, _walk.Z));
            return false;
        });
        Add("a shorter view distance drops the far scenery", frame =>
        {
            if (frame == 0)
            {
                _streamer.ViewDistance = 256f;
                return false;
            }
            Check(_streamer.Scenery.Count == Wanted(256f) && _streamer.Scenery.Count < _far, $"fewer chunks are drawn ({_streamer.Scenery.Count} < {_far})");
            return true;
        });
        Add("the shortest draws the simulated ground alone", frame =>
        {
            if (frame == 0)
            {
                _streamer.ViewDistance = FarChunks.MinViewDistance;
                return false;
            }
            Check(_streamer.Scenery.Count == Wanted(FarChunks.MinViewDistance), $"scenery matches what is in view ({_streamer.Scenery.Count})");
            Check(_streamer.Chunks.Count > 0, "the simulated ground is still there");
            return true;
        });
    }

    private int Wanted(float distance)
    {
        var loaded = new HashSet<ChunkCoord>(_streamer.Chunks.Keys);
        return FarChunks.Wanted(_main.LocalPlayer!.GlobalPosition.ToNumerics(), distance, 64f, _streamer.Streamer.InWorld, loaded).Count;
    }
}
