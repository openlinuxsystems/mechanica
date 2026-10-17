using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.World;
using Mechanica.Game.Menus;
using Mechanica.Game.World;

namespace Mechanica.Game.Tests;

/// <summary>
/// View distance draws the world past the simulated ground (UI spec §16 Graphics). With scenery on, chunks
/// fill in out to the view distance, nearest first, never overlapping the simulated chunks and with no
/// colliders; a shorter view distance drops the far ones and the shortest draws the simulated ground alone.
/// </summary>
public partial class SceneryTest : StagedTest
{
    private Main _main = null!;
    private WorldStreamer _streamer = null!;
    private int _far;

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
