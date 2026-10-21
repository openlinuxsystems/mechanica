using Godot;
using Mechanica.Core.World;
using Mechanica.Game.Mods;

namespace Mechanica.Game.World;

/// <summary>
/// A world object drawn as its own copy of an animated model (<see cref="MeshDefinition.Clips"/>, the Bloom Pods) instead
/// of being batched with its neighbours: it plays its <see cref="ObjectClips.Appear"/> clip when its chunk comes in, loops
/// <see cref="ObjectClips.Idle"/>, plays <see cref="ObjectClips.Hit"/> when struck, and gives off its
/// <see cref="WorldObjectDefinition.Glow"/>. <see cref="Wreck"/> makes the copy that plays the break where one broke.
/// Cosmetic only.
/// </summary>
public partial class AnimatedObject : Node3D
{
    // Each model file's scene, loaded once and copied for every object; null when it failed to load.
    private static readonly Dictionary<string, Node3D?> Scenes = [];

    private AnimationPlayer? _player;
    private OmniLight3D? _light;
    private ObjectGlow? _glow;
    private double _age;
    private float _flash;

    /// <summary>Which object this draws.</summary>
    public WorldObjectId Id { get; private set; }

    /// <summary>The clips it plays.</summary>
    public ObjectClips Clips { get; private set; } = new();

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
    /// A copy of <paramref name="file"/>'s model for <paramref name="o"/> at <paramref name="placed"/> (relative to its
    /// parent), playing its appear clip when <paramref name="appear"/>, else its idle; with its glow when
    /// <paramref name="lit"/>. Null when the model fails to load.
    /// </summary>
    public static AnimatedObject? Create(string file, WorldObjectPlacement o, Transform3D placed, bool appear, bool lit)
    {
        if (Load(file) is not { } template || o.Definition.Mesh?.Clips is not { } clips)
            return null;
        var node = new AnimatedObject { Name = $"{o.Definition.Id}_{o.Id}", Id = o.Id, Clips = clips, Transform = placed };
        var model = (Node3D)template.Duplicate();
        model.Position = o.Definition.Mesh.At.ToGodot();
        node.AddChild(model);
        node._player = model.FindChildren("*", nameof(AnimationPlayer), owned: false).OfType<AnimationPlayer>().FirstOrDefault();
        // The idle clip loops; a glTF file cannot say so.
        if (clips.Idle is { } idle && node.Has(idle))
            node._player!.GetAnimation(idle).LoopMode = Animation.LoopModeEnum.Linear;
        if (lit && o.Definition.Glow is { } glow)
            node.Light(glow);
        node.Start(appear ? clips.Appear : null);
        return node;
    }

    /// <summary>
    /// A copy of <paramref name="o"/>'s model at its place in the world that plays the break clip, flares its glow and
    /// then shrinks away and frees itself after <see cref="ObjectClips.BreakSeconds"/>. Null when it has no break clip.
    /// </summary>
    public static AnimatedObject? Wreck(string file, WorldObjectPlacement o)
    {
        if (o.Definition.Mesh?.Clips is not { Break: { } broke } clips)
            return null;
        var placed = new Transform3D(TerrainChunk.Turn(o).Scaled(new Vector3(o.Scale, o.Scale, o.Scale)), o.Position.ToGodot());
        if (Create(file, o, placed, false, true) is not { } node)
            return null;
        node.Name = $"{o.Definition.Id}_wreck";
        node._flash = 4f;
        node._player?.Stop();
        node.PlayOnce(broke, null);
        var tween = node.CreateTween();
        tween.TweenInterval(Math.Max(0, clips.BreakSeconds - clips.FadeSeconds));
        tween.TweenProperty(node, "scale", node.Scale * 0.01f, Math.Max(0.01, clips.FadeSeconds)).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(node.QueueFree));
        return node;
    }

    /// <summary>Plays the hit clip, then goes back to idle. False when there is no hit clip.</summary>
    public bool Hit()
    {
        if (Clips.Hit is not { } hit || !Has(hit))
            return false;
        _flash = 1.5f;
        PlayOnce(hit, Clips.Idle);
        return true;
    }

    public override void _Process(double delta)
    {
        if (_light is null || _glow is null)
            return;
        _age += delta;
        _flash = Mathf.MoveToward(_flash, 0f, (float)delta * 3f);
        var swell = _glow.Pulse > 0 ? 0.8f + 0.2f * Mathf.Sin((float)(_age * Mathf.Tau / _glow.Pulse)) : 1f;
        _light.LightEnergy = _glow.Energy * (swell + _flash);
    }

    private void Light(ObjectGlow glow)
    {
        var c = WorldGenData.ParseColor(glow.Color ?? "#ffffff");
        _glow = glow;
        _light = new OmniLight3D
        {
            Name = "Glow",
            LightColor = new Color(c.X, c.Y, c.Z),
            LightEnergy = glow.Energy,
            OmniRange = glow.Range,
            Position = glow.At.ToGodot(),
            ShadowEnabled = false,
            DistanceFadeEnabled = true,
            DistanceFadeBegin = 60f,
            DistanceFadeLength = 20f,
        };
        AddChild(_light);
        // Start each pod's swell somewhere different, so neighbours do not pulse together.
        _age = (uint)Id.GetHashCode() % 1000 / 1000.0 * Math.Max(glow.Pulse, 1f);
    }

    private void Start(string? appear)
    {
        if (appear is not null && Has(appear))
            PlayOnce(appear, Clips.Idle);
        else if (Clips.Idle is { } idle && Has(idle))
            _player!.Play(idle);
    }

    private void PlayOnce(string clip, string? then)
    {
        if (_player is null || !Has(clip))
            return;
        _player.ClearQueue();
        _player.Play(clip);
        _player.Seek(0, update: true);
        if (then is not null && Has(then))
            _player.Queue(then);
    }

    private bool Has(string clip) => _player?.HasAnimation(clip) == true;

    private static Node3D? Load(string file)
    {
        if (Scenes.TryGetValue(file, out var cached))
            return cached;
        var scene = ModGltf.Load(file) as Node3D;
        if (scene is null)
            GD.PushWarning($"[world] '{file}' did not load as glTF.");
        return Scenes[file] = scene;
    }
}
