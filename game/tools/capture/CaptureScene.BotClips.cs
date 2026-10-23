using Godot;
using Mechanica.Core.Settings;
using Mechanica.Game.Building;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Tools;

// Given `bot_clips`, the capture plays every clip of every vanilla bot model (mods/vanilla/meshes/bots/*.glb) on the
// ground of a meadow and records each as a run of frames (OUT/<model>_<clip>/0000.png ...), seen three-quarter from the
// front (tools/capture-bot-clips.sh turns them into clips). A third argument keeps the models it names (comma-separated).
// The model plays as loaded, without the bot's foot planting or effects, so a clip shows as it was authored.
public partial class CaptureScene
{
    private Node3D? _clipModel;
    private AnimationPlayer? _clipPlayer;

    private void BotClipShots(string? only)
    {
        var anchor = Vector3.Zero;
        Shot("botclips_00_go", 120, () =>
        {
            Engine.TimeScale = 1.0;
            var g = _main.Streamer.Generator;
            GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off } });
            Go(Find(p => g.BiomeAt(p.X, p.Y).Id == "meadow" && g.HeightAt(p.X, p.Y) > g.SeaLevel + 1f && g.SlopeAt(p.X, p.Y) < 0.04f
                && !g.PoisAt(p.X, p.Y).Any()));
        });
        Shot("botclips_00_place", 30, () =>
        {
            var at = Player.GlobalPosition + new Vector3(0, 0, -6f);
            anchor = at with { Y = _main.Streamer.Generator.HeightAt(at.X, at.Z) };
            Player.ThirdPerson = false;
        });
        var directory = System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "mods", "vanilla", "meshes", "bots");
        foreach (var file in System.IO.Directory.GetFiles(directory, "*.glb").Order(StringComparer.Ordinal))
        {
            var model = System.IO.Path.GetFileNameWithoutExtension(file);
            if (only is { Length: > 0 } && !only.Split(',').Any(o => model == o))
                continue;
            foreach (var clip in ClipNames(file))
            {
                var name = $"{model}_{clip.Name}";
                var frames = Math.Clamp((int)MathF.Ceiling(clip.Seconds * 30f) + 6, 20, 150);
                Shot(name, frames, () =>
                {
                    _clipModel?.QueueFree();
                    _clipModel = _main.Assets.InstantiateScene($"meshes/bots/{model}.glb", "vanilla");
                    if (_clipModel is null)
                        return;
                    AddChild(_clipModel);
                    _clipModel.GlobalPosition = anchor;
                    _clipPlayer = LiftBody.FindPlayer(_clipModel);
                    _clipPlayer?.Play(clip.Name);
                    var size = model.StartsWith("thresher", StringComparison.Ordinal) ? 1.7f : model.Contains("calf", StringComparison.Ordinal) ? 0.8f : 1.1f;
                    _follow = null;
                    _camera.MakeCurrent();
                    _camera.GlobalPosition = anchor + new Vector3(2.6f, 1.5f, -3.2f) * size;
                    _camera.LookAt(anchor + Vector3.Up * 0.55f * size);
                    _frame = 0;
                    _time = 0.5;
                    DirAccess.MakeDirRecursiveAbsolute($"{_out}/{name}");
                });
                _recording[name] = _ => { };
            }
        }
    }

    // The clips in a glb and their lengths, read from its JSON chunk.
    private static IEnumerable<(string Name, float Seconds)> ClipNames(string file)
    {
        var bytes = System.IO.File.ReadAllBytes(file);
        var length = BitConverter.ToInt32(bytes, 12);
        using var json = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(20, length));
        var root = json.RootElement;
        if (!root.TryGetProperty("animations", out var animations))
            yield break;
        var accessors = root.GetProperty("accessors");
        foreach (var animation in animations.EnumerateArray())
        {
            var seconds = 0f;
            foreach (var sampler in animation.GetProperty("samplers").EnumerateArray())
            {
                var input = accessors[sampler.GetProperty("input").GetInt32()];
                if (input.TryGetProperty("max", out var max))
                    seconds = MathF.Max(seconds, max[0].GetSingle());
            }
            yield return (animation.GetProperty("name").GetString() ?? "", seconds);
        }
    }
}
