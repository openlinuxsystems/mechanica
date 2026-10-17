using System.Buffers.Binary;
using System.Text.Json;

namespace Mechanica.Core.Characters;

/// <summary>One animation in a glTF file: its name and the nodes (by name) it moves.</summary>
/// <param name="Name">Animation name.</param>
/// <param name="Duration">Last keyframe time in seconds.</param>
/// <param name="Targets">Names of the nodes its channels animate.</param>
public sealed record GltfAnimation(string Name, float Duration, IReadOnlySet<string> Targets);

/// <summary>
/// What the loader needs to know about a glTF model without the engine: the joints of its first skin and
/// its animations. It reads only the JSON (a <c>.gltf</c> file, or the JSON chunk of a <c>.glb</c>), so
/// Core can check that a character's mesh fits <see cref="CharacterRig"/>.
/// </summary>
/// <param name="Joints">Joint names of the first skin, in order; empty when the file has no skin.</param>
/// <param name="SkinnedMeshes">How many nodes draw a mesh with a skin.</param>
/// <param name="Animations">Every animation, in file order.</param>
public sealed record GltfSummary(IReadOnlyList<string> Joints, int SkinnedMeshes, IReadOnlyList<GltfAnimation> Animations)
{
    private const uint GlbMagic = 0x46546C67; // "glTF"
    private const uint JsonChunk = 0x4E4F534A; // "JSON"

    /// <summary>Reads a <c>.glb</c> or <c>.gltf</c> file.</summary>
    /// <exception cref="FormatException">The file is not glTF 2.0 or its JSON is malformed.</exception>
    public static GltfSummary Read(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Reads glTF from bytes: binary (<c>.glb</c>) when it starts with the glTF magic, JSON otherwise.</summary>
    /// <exception cref="FormatException">The data is not glTF 2.0 or its JSON is malformed.</exception>
    public static GltfSummary Parse(ReadOnlySpan<byte> bytes)
    {
        var json = bytes.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == GlbMagic ? JsonOf(bytes) : bytes;
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            return FromJson(document.RootElement);
        }
        catch (JsonException e)
        {
            throw new FormatException($"glTF JSON is malformed: {e.Message}", e);
        }
        catch (InvalidOperationException e)
        {
            throw new FormatException($"glTF JSON has an unexpected shape: {e.Message}", e);
        }
    }

    private static ReadOnlySpan<byte> JsonOf(ReadOnlySpan<byte> glb)
    {
        if (glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(glb[4..]) != 2)
            throw new FormatException("not a glTF 2.0 binary file.");
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb[12..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(glb[16..]) != JsonChunk || length < 0 || 20 + length > glb.Length)
            throw new FormatException("the glTF binary file does not start with a JSON chunk.");
        return glb.Slice(20, length);
    }

    private static GltfSummary FromJson(JsonElement root)
    {
        var nodes = Array(root, "nodes");
        string NodeName(int index) =>
            index >= 0 && index < nodes.Count && nodes[index].TryGetProperty("name", out var n) ? n.GetString() ?? $"node{index}" : $"node{index}";

        var skins = Array(root, "skins");
        var joints = skins.Count == 0 ? [] : Array(skins[0], "joints").Select(j => NodeName(j.GetInt32())).ToList();
        var skinned = nodes.Count(n => n.TryGetProperty("mesh", out _) && n.TryGetProperty("skin", out _));

        var accessors = Array(root, "accessors");
        var animations = new List<GltfAnimation>();
        foreach (var (animation, i) in Array(root, "animations").Select((a, i) => (a, i)))
        {
            var samplers = Array(animation, "samplers");
            var duration = 0f;
            foreach (var sampler in samplers)
            {
                var input = sampler.GetProperty("input").GetInt32();
                if (input >= 0 && input < accessors.Count && accessors[input].TryGetProperty("max", out var max) && max.GetArrayLength() > 0)
                    duration = MathF.Max(duration, max[0].GetSingle());
            }
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var channel in Array(animation, "channels"))
            {
                if (channel.TryGetProperty("target", out var target) && target.TryGetProperty("node", out var node))
                    targets.Add(NodeName(node.GetInt32()));
            }
            var name = animation.TryGetProperty("name", out var n) ? n.GetString() ?? $"animation{i}" : $"animation{i}";
            animations.Add(new GltfAnimation(name, duration, targets));
        }
        return new GltfSummary(joints, skinned, animations);
    }

    private static List<JsonElement> Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? [.. array.EnumerateArray()] : [];

    /// <summary>The animation named <paramref name="name"/>, or null.</summary>
    public GltfAnimation? Animation(string name) => Animations.FirstOrDefault(a => a.Name == name);

    /// <summary>Rig bones missing from <see cref="Joints"/>.</summary>
    public IEnumerable<string> MissingBones() => CharacterRig.Bones.Where(b => !Joints.Contains(b));
}
