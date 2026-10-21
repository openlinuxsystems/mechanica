using Godot;
using Mechanica.Core.World;
using Mechanica.Game.Mods;

namespace Mechanica.Game.World;

/// <summary>
/// A POI model that follows a quest (<see cref="PoiModel.Story"/>, the SV-04's battery bay): its own copy of the animated
/// model, looping <see cref="PoiModelStory.Waiting"/> until the quest is done, playing <see cref="PoiModelStory.Finish"/>
/// once when it gets done while the model is in view, then looping <see cref="PoiModelStory.Done"/>. It asks
/// <see cref="QuestDone"/> every frame, so every peer plays it from the quest state it has. Cosmetic only.
/// </summary>
public partial class StoryModel : Node3D
{
    // Each model file's scene, loaded once and copied for every placement; null when it failed to load.
    private static readonly Dictionary<string, Node3D?> Scenes = [];

    private AnimationPlayer? _player;
    private bool _done;

    /// <summary>The quest it follows (qualified).</summary>
    public string Quest { get; private set; } = "";

    /// <summary>Its clips.</summary>
    public PoiModelStory Clips { get; private set; } = new();

    /// <summary>Whether a quest is done; null treats every quest as not done.</summary>
    public Func<string, bool>? QuestDone { get; private set; }

    /// <summary>The clip playing now, or null (tests).</summary>
    public string? Playing => _player is { } p && p.IsPlaying() ? p.CurrentAnimation : null;

    /// <summary>Forgets the loaded models, so a new world reads its mods' files afresh.</summary>
    public static void ClearModels()
    {
        foreach (var scene in Scenes.Values)
            scene?.Free();
        Scenes.Clear();
    }

    /// <summary>
    /// A copy of <paramref name="file"/>'s model at <paramref name="transform"/> following <paramref name="quest"/>, already
    /// in the pose its state calls for (no finish clip for a quest done before it came into view). Null when the model
    /// fails to load.
    /// </summary>
    public static StoryModel? Create(string file, string name, Transform3D transform, string quest, PoiModelStory clips, Func<string, bool>? questDone)
    {
        if (Load(file) is not { } template)
            return null;
        var node = new StoryModel { Name = name, Transform = transform, Quest = quest, Clips = clips, QuestDone = questDone };
        var model = (Node3D)template.Duplicate();
        node.AddChild(model);
        node._player = model.FindChildren("*", nameof(AnimationPlayer), owned: false).OfType<AnimationPlayer>().FirstOrDefault();
        // The looping clips loop; a glTF file cannot say so.
        foreach (var loop in new[] { clips.Waiting, clips.Done })
        {
            if (loop is not null && node.Has(loop))
                node._player!.GetAnimation(loop).LoopMode = Animation.LoopModeEnum.Linear;
        }
        node._done = questDone?.Invoke(quest) == true;
        node.Play(node._done ? clips.Done : clips.Waiting);
        return node;
    }

    public override void _Process(double delta)
    {
        if (_done || QuestDone?.Invoke(Quest) != true)
            return;
        _done = true;
        if (Clips.Finish is { } finish && Has(finish))
        {
            _player!.Play(finish);
            _player.Seek(0, update: true);
            if (Clips.Done is { } done && Has(done))
                _player.Queue(done);
        }
        else
        {
            Play(Clips.Done);
        }
    }

    private void Play(string? clip)
    {
        if (clip is null || !Has(clip))
        {
            _player?.Stop();
            return;
        }
        _player!.ClearQueue();
        _player.Play(clip);
    }

    private bool Has(string clip) => _player?.HasAnimation(clip) == true;

    private static Node3D? Load(string file)
    {
        if (Scenes.TryGetValue(file, out var cached))
            return cached;
        var scene = ModGltf.Load(file) as Node3D;
        if (scene is null)
            GD.PushWarning($"[world] '{file}' did not load as glTF.");
        // The collider mesh is for StructureModel's shapes, never drawn.
        foreach (var collider in scene?.FindChildren("collider*", nameof(MeshInstance3D), owned: false) ?? [])
        {
            collider.GetParent().RemoveChild(collider);
            collider.Free();
        }
        return Scenes[file] = scene;
    }
}
