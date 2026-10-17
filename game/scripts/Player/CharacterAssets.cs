using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Player;

/// <summary>
/// The player characters' models, the shared animation set and the hand-held props, loaded at runtime from
/// mod folders with <see cref="GltfDocument"/> (like part meshes) and cached, so every player of a character
/// shares one mesh and every player shares one set of clips.
/// </summary>
public sealed class CharacterAssets
{
    /// <summary>Godot path of the animation tuning.</summary>
    public const string DefaultSettingsPath = "res://data/character.json";

    private readonly Func<string, string?> _modDirectory;
    private readonly Dictionary<string, Node3D?> _models = [];
    private Dictionary<string, Animation>? _clips;
    private Dictionary<string, Node3D>? _props;

    /// <summary>Finds mod folders with <paramref name="modDirectory"/> (mod id → folder, or null).</summary>
    public CharacterAssets(Func<string, string?> modDirectory, CharacterSettings settings)
    {
        _modDirectory = modDirectory;
        Settings = settings;
    }

    /// <summary>Uses the folders of the mods in <paramref name="content"/>.</summary>
    public CharacterAssets(ModLoadResult content, CharacterSettings? settings = null)
        : this(id => content.Mods.FirstOrDefault(m => m.Id == id)?.Directory, settings ?? LoadDefaultSettings())
    {
    }

    /// <summary>Animation tuning.</summary>
    public CharacterSettings Settings { get; }

    /// <summary>Loads <see cref="DefaultSettingsPath"/>, failing loudly on bad data.</summary>
    public static CharacterSettings LoadDefaultSettings()
    {
        using var file = FileAccess.Open(DefaultSettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {DefaultSettingsPath}: {FileAccess.GetOpenError()}");
        return CharacterSettings.FromJson(file.GetAsText());
    }

    /// <summary>Every clip of the animation set by name; empty when it fails to load.</summary>
    public IReadOnlyDictionary<string, Animation> Clips => _clips ??= LoadClips();

    /// <summary>Clip lengths in seconds, by name, for <see cref="CharacterAnimator"/>.</summary>
    public IReadOnlyDictionary<string, float> ClipLengths => Clips.ToDictionary(c => c.Key, c => c.Value.Length);

    /// <summary>
    /// A new instance of <paramref name="character"/>'s model (a scene holding a <see cref="Skeleton3D"/>), or null
    /// when its mesh fails to load.
    /// </summary>
    public Node3D? Instantiate(Character character)
    {
        var key = character.Mesh.ToString();
        if (!_models.TryGetValue(key, out var template))
            _models[key] = template = Load(character.Mesh, $"character '{character.QualifiedId}'");
        return template?.Duplicate() as Node3D;
    }

    /// <summary>
    /// A new instance of the prop node for <paramref name="prop"/> and where it sits in the rest-pose character's
    /// right hand (character model space), or null when nothing is drawn for it.
    /// </summary>
    public (Node3D Node, Transform3D Rest)? Prop(CharacterProp prop)
    {
        _props ??= LoadProps();
        if (CharacterRig.PropNode(prop) is not { } name || !_props.TryGetValue(name, out var template))
            return null;
        var node = (Node3D)template.Duplicate();
        var rest = node.Transform;
        node.Transform = Transform3D.Identity;
        return (node, rest);
    }

    private Dictionary<string, Animation> LoadClips()
    {
        var clips = new Dictionary<string, Animation>(StringComparer.Ordinal);
        if (Parse(Settings.Animations, "animations") is not { } path || Load(path, "the character animations") is not { } root)
            return clips;
        foreach (var player in root.FindChildren("*", nameof(AnimationPlayer), owned: false).OfType<AnimationPlayer>())
        {
            foreach (var name in player.GetAnimationList())
                clips[name.ToString()] = player.GetAnimation(name);
        }
        root.Free();
        foreach (var missing in CharacterRig.Clips.Where(c => !clips.ContainsKey(c)))
            GD.PushWarning($"[characters] the animation set has no clip '{missing}'.");
        return clips;
    }

    private Dictionary<string, Node3D> LoadProps()
    {
        var props = new Dictionary<string, Node3D>(StringComparer.Ordinal);
        if (Parse(Settings.HandTools, "hand_tools") is not { } path || Load(path, "the hand tools") is not { } root)
            return props;
        var wanted = Enum.GetValues<CharacterProp>().Select(CharacterRig.PropNode).OfType<string>().ToHashSet();
        foreach (var node in root.FindChildren("*", nameof(Node3D), owned: false).OfType<Node3D>().ToList())
        {
            var name = node.Name.ToString();
            if (!wanted.Contains(name) || props.ContainsKey(name))
                continue;
            var rest = RootSpace(node, root);
            node.GetParent().RemoveChild(node);
            node.Transform = rest;
            props[name] = node;
        }
        root.Free();
        return props;
    }

    // A node's transform relative to the scene root (the root's own transform is the file's placement).
    private static Transform3D RootSpace(Node3D node, Node3D root)
    {
        var transform = node.Transform;
        for (var parent = node.GetParent(); parent is not null && parent != root; parent = parent.GetParent())
        {
            if (parent is Node3D p)
                transform = p.Transform * transform;
        }
        return transform;
    }

    private static AssetPath? Parse(string text, string what)
    {
        if (AssetPath.TryParse(text, Identifiers.VanillaModId, AssetKind.Mesh, out var path, out var error))
            return path;
        GD.PushWarning($"[characters] {what}: {error}");
        return null;
    }

    private Node3D? Load(AssetPath path, string what)
    {
        if (_modDirectory(path.ModId) is not { } directory)
        {
            GD.PushWarning($"[characters] {what}: mod '{path.ModId}' is not loaded.");
            return null;
        }
        var document = new GltfDocument();
        var state = new GltfState();
        var file = path.Resolve(directory);
        if (document.AppendFromFile(file, state) != Error.Ok || document.GenerateScene(state) is not Node3D root)
        {
            GD.PushWarning($"[characters] {what}: '{path}' did not load as glTF.");
            return null;
        }
        return root;
    }
}
