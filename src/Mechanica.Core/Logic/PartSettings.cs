using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.Logic;

/// <summary>
/// The values of a placed part's <see cref="PartDefinition.Settings"/>: checking a value a player picked, and
/// filling in defaults. A placed part stores only what was changed (<see cref="Creations.PartInstance.Settings"/>).
/// </summary>
public static class PartSettings
{
    /// <summary>The definition of setting <paramref name="id"/>, or null when the part has none by that id.</summary>
    public static SettingDefinition? Find(PartDefinition part, string id) =>
        part.Settings?.FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// The part with a sequence setting (a Controller) linked into <paramref name="partId"/> while that part's menu
    /// locks for one (<see cref="MenuDefinition.LockWhenSequenced"/>), or null when its settings are free to change.
    /// </summary>
    public static int? Sequencer(Creations.Creation creation, int partId)
    {
        if (creation.DefinitionOf(partId).Menu?.LockWhenSequenced != true)
            return null;
        foreach (var from in creation.LinksTo(partId))
        {
            if (creation.DefinitionOf(from).Settings?.Any(s => s.Type == SettingType.Sequence) == true)
                return from;
        }
        return null;
    }

    /// <summary>The value a part starts with (see <see cref="SettingDefinition.Default"/>).</summary>
    public static JsonElement DefaultOf(SettingDefinition setting) => setting.Default ?? setting.Type switch
    {
        SettingType.Number => Element(setting.Min ?? 0f),
        SettingType.Choice => Element(setting.Options?.FirstOrDefault() ?? ""),
        SettingType.Toggle => Element(false),
        SettingType.Sequence => Element(Array.Empty<object>()),
        _ => Element(Array.Empty<float>()),
    };

    /// <summary>Null when <paramref name="value"/> fits <paramref name="setting"/>, or why it does not.</summary>
    public static string? Check(SettingDefinition setting, JsonElement value)
    {
        var name = setting.Name ?? setting.Id;
        switch (setting.Type)
        {
            case SettingType.Number:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number) || !float.IsFinite(number))
                    return $"{name} must be a number.";
                return InRange(setting, number) ? null : $"{name} must be between {setting.Min:0.###} and {setting.Max:0.###}.";
            case SettingType.Choice:
                return value.ValueKind == JsonValueKind.String && setting.Options?.Contains(value.GetString()!) == true
                    ? null
                    : $"{name} must be one of {string.Join(", ", setting.Options ?? [])}.";
            case SettingType.Toggle:
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : $"{name} must be true or false.";
            case SettingType.NumberList:
                if (value.ValueKind != JsonValueKind.Array)
                    return $"{name} must be a list of numbers.";
                if (value.GetArrayLength() > (setting.MaxCount ?? 0))
                    return $"{name} holds at most {setting.MaxCount} numbers.";
                foreach (var entry in value.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Number || !entry.TryGetSingle(out var n) || !float.IsFinite(n) || !InRange(setting, n))
                        return $"each of {name} must be a number between {setting.Min:0.###} and {setting.Max:0.###}.";
                }
                return null;
            case SettingType.Sequence:
                return CheckSequence(setting, name, value);
            default:
                return $"{name} has no type.";
        }
    }

    /// <summary>
    /// Null when every entry of <paramref name="values"/> names a setting of <paramref name="part"/> and fits it,
    /// or the first problem.
    /// </summary>
    public static string? Check(PartDefinition part, IReadOnlyDictionary<string, JsonElement> values)
    {
        foreach (var (id, value) in values)
        {
            if (Find(part, id) is not { } setting)
                return $"'{part.Name ?? part.Id}' has no setting '{id}'.";
            if (Check(setting, value) is { } error)
                return error;
        }
        return null;
    }

    /// <summary>The value of setting <paramref name="id"/>: the stored one, else its default. Null when the part has no such setting.</summary>
    public static JsonElement? Value(PartDefinition part, IReadOnlyDictionary<string, JsonElement>? stored, string id)
    {
        if (Find(part, id) is not { } setting)
            return null;
        return stored is not null && stored.TryGetValue(id, out var value) ? value : DefaultOf(setting);
    }

    /// <summary>
    /// True when the part's direction is reversed: it names a reverse setting (<see cref="LogicDefinition.Reverse"/>)
    /// and that toggle is on.
    /// </summary>
    public static bool Reversed(PartDefinition part, IReadOnlyDictionary<string, JsonElement>? stored) =>
        part.Logic?.Reverse is { } id && Value(part, stored, id) is { ValueKind: JsonValueKind.True };

    /// <summary>Every setting's value (stored or default), in the definition's order.</summary>
    public static IReadOnlyList<(string Id, JsonElement Value)> All(PartDefinition part, IReadOnlyDictionary<string, JsonElement>? stored) =>
        [.. (part.Settings ?? []).Select(s => (s.Id!, stored is not null && stored.TryGetValue(s.Id!, out var v) ? v : DefaultOf(s)))];

    /// <summary>
    /// <paramref name="stored"/> with <paramref name="changes"/> applied: a value sets a setting, null puts it
    /// back to its default. Values equal to the default are dropped, so an untouched part stores nothing; the
    /// result is null when nothing is left. Keys come out sorted.
    /// </summary>
    public static IReadOnlyDictionary<string, JsonElement>? Apply(PartDefinition part, IReadOnlyDictionary<string, JsonElement>? stored,
        IReadOnlyDictionary<string, JsonElement?> changes)
    {
        var result = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (id, value) in stored ?? new Dictionary<string, JsonElement>())
            result[id] = value;
        foreach (var (id, value) in changes)
        {
            if (value is not { } set || Find(part, id) is not { } setting || JsonElement.DeepEquals(set, DefaultOf(setting)))
                result.Remove(id);
            else
                result[id] = set.Clone();
        }
        return result.Count == 0 ? null : result;
    }

    /// <summary>Most joints one sequence frame may list.</summary>
    public const int MaxFrameJoints = 32;

    /// <summary>
    /// The frames of a sequence value (<see cref="SettingType.Sequence"/>), each a list of joint targets. A plain-number
    /// frame comes back as <paramref name="linkedBearings"/> all at that angle, at <paramref name="defaultSpeed"/>.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<SequenceStep>> Frames(JsonElement value, IReadOnlyList<int> linkedBearings, float defaultSpeed)
    {
        var frames = new List<IReadOnlyList<SequenceStep>>();
        if (value.ValueKind != JsonValueKind.Array)
            return frames;
        foreach (var frame in value.EnumerateArray())
        {
            if (frame.ValueKind == JsonValueKind.Number)
                frames.Add([.. linkedBearings.Select(b => new SequenceStep(b, frame.GetSingle(), defaultSpeed))]);
            else if (frame.ValueKind == JsonValueKind.Array)
                frames.Add([.. frame.EnumerateArray().Select(e => new SequenceStep(e.GetProperty("part").GetInt32(), e.GetProperty("target").GetSingle(), e.GetProperty("speed").GetSingle()))]);
        }
        return frames;
    }

    /// <summary>A sequence value holding <paramref name="frames"/>, as <c>ConfigurePart</c> sends it.</summary>
    public static JsonElement Sequence(IEnumerable<IEnumerable<SequenceStep>> frames) =>
        Element(frames.Select(f => f.Select(s => new Dictionary<string, object> { ["part"] = s.Part, ["target"] = s.Target, ["speed"] = s.Speed }).ToList()).ToList());

    private static string? CheckSequence(SettingDefinition setting, string? name, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return $"{name} must be a list of frames.";
        if (value.GetArrayLength() > (setting.MaxCount ?? 0))
            return $"{name} holds at most {setting.MaxCount} frames.";
        foreach (var frame in value.EnumerateArray())
        {
            if (frame.ValueKind == JsonValueKind.Number)
            {
                if (!frame.TryGetSingle(out var angle) || !float.IsFinite(angle) || !InRange(setting, angle))
                    return $"each frame of {name} must be between {setting.Min:0.###} and {setting.Max:0.###}.";
                continue;
            }
            if (frame.ValueKind != JsonValueKind.Array)
                return $"each frame of {name} must be a list of joints.";
            if (frame.GetArrayLength() > MaxFrameJoints)
                return $"a frame of {name} lists at most {MaxFrameJoints} joints.";
            var parts = new HashSet<int>();
            foreach (var step in frame.EnumerateArray())
            {
                if (step.ValueKind != JsonValueKind.Object
                    || !step.TryGetProperty("part", out var part) || part.ValueKind != JsonValueKind.Number || !part.TryGetInt32(out var id)
                    || !step.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.Number
                    || !step.TryGetProperty("speed", out var speed) || speed.ValueKind != JsonValueKind.Number
                    || step.EnumerateObject().Count() != 3)
                    return $"each joint of {name} is {{\"part\", \"target\", \"speed\"}}.";
                if (!parts.Add(id))
                    return $"a frame of {name} lists part {id} twice.";
                if (!target.TryGetSingle(out var t) || !float.IsFinite(t) || !InRange(setting, t))
                    return $"each target of {name} must be between {setting.Min:0.###} and {setting.Max:0.###}.";
                if (!speed.TryGetSingle(out var v) || !float.IsFinite(v) || v <= 0 || v > (setting.MaxSpeed ?? 0))
                    return $"each speed of {name} must be more than 0 and at most {setting.MaxSpeed:0.###}.";
            }
        }
        return null;
    }

    private static bool InRange(SettingDefinition setting, float value) =>
        value >= (setting.Min ?? float.NegativeInfinity) && value <= (setting.Max ?? float.PositiveInfinity);

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value);
}

/// <summary>One joint's target in a sequence frame.</summary>
/// <param name="Part">The bearing or piston part.</param>
/// <param name="Target">A bearing's angle in degrees or a piston's extension in m.</param>
/// <param name="Speed">How fast it gets there: rad/s for a bearing, m/s for a piston.</param>
public readonly record struct SequenceStep(int Part, float Target, float Speed);
