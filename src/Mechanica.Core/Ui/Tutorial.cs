using System.Text.Json;
using System.Text.RegularExpressions;
using Mechanica.Core.Data;

namespace Mechanica.Core.Ui;

/// <summary>One tutorial tip: shown once, the first time its trigger happens.</summary>
/// <param name="Id">Stable id, remembered once seen.</param>
/// <param name="Trigger">What shows it: <c>start</c>, <c>tool:hammer</c>, <c>screen:inventory</c>, ….</param>
/// <param name="Text">The tip. <c>{action_id}</c> becomes that action's key, so tips follow the player's bindings.</param>
public sealed record TutorialHint(string Id, string Trigger, string Text);

/// <summary>
/// The tutorial tips (UI spec §16 Gameplay, "Show tutorial hints") and the loading screen's tips, from
/// <c>game/data/tutorial.json</c>; both may use <c>{action_id}</c> keys. Tips explain the engine's own tools and screens, so they ship with the
/// build rather than in a mod.
/// </summary>
public sealed partial class Tutorial
{
    private sealed record TutorialFile
    {
        public List<TutorialHint>? Hints { get; init; }
        public List<string>? Tips { get; init; }
        public float? HintSeconds { get; init; }
    }

    /// <summary>No tips.</summary>
    public static Tutorial Empty { get; } = new([], [], DefaultHintSeconds);

    /// <summary>Seconds a hint stays up when the file doesn't say.</summary>
    public const float DefaultHintSeconds = 8f;

    private Tutorial(IReadOnlyList<TutorialHint> hints, IReadOnlyList<string> tips, float hintSeconds)
    {
        Hints = hints;
        Tips = tips;
        HintSeconds = hintSeconds;
    }

    /// <summary>Seconds each hint stays on screen.</summary>
    public float HintSeconds { get; }

    /// <summary>The first-time tips.</summary>
    public IReadOnlyList<TutorialHint> Hints { get; }

    /// <summary>Tips the loading screen cycles through.</summary>
    public IReadOnlyList<string> Tips { get; }

    /// <summary>Reads <paramref name="json"/>; throws <see cref="FormatException"/> on bad data (a duplicate id, an empty tip).</summary>
    public static Tutorial Parse(string json)
    {
        TutorialFile? file;
        try
        {
            file = DataJson.Read<TutorialFile>(json);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid tutorial JSON: {e.Message}", e);
        }
        var hints = file?.Hints ?? [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hint in hints)
        {
            if (string.IsNullOrWhiteSpace(hint?.Id) || string.IsNullOrWhiteSpace(hint.Trigger) || string.IsNullOrWhiteSpace(hint.Text))
                throw new FormatException("Every tutorial hint needs an id, a trigger and text.");
            if (!ids.Add(hint.Id))
                throw new FormatException($"Tutorial hint '{hint.Id}' is listed twice.");
        }
        var tips = file?.Tips ?? [];
        if (tips.Any(string.IsNullOrWhiteSpace))
            throw new FormatException("Loading tips must not be empty.");
        var seconds = file?.HintSeconds ?? DefaultHintSeconds;
        if (!float.IsFinite(seconds) || seconds <= 0)
            throw new FormatException("hint_seconds must be a positive number.");
        return new Tutorial(hints, tips, seconds);
    }

    /// <summary>The tip <paramref name="trigger"/> shows now: the first for it not in <paramref name="seen"/>, or null.</summary>
    public TutorialHint? Next(string trigger, IReadOnlySet<string> seen) =>
        Hints.FirstOrDefault(h => h.Trigger == trigger && !seen.Contains(h.Id));

    /// <summary>Puts each <c>{action_id}</c> in <paramref name="text"/> through <paramref name="key"/> (the action's key label); unknown names stay as they are.</summary>
    public static string Fill(string text, Func<string, string?> key) =>
        Placeholder().Replace(text, m => key(m.Groups[1].Value) ?? m.Value);

    [GeneratedRegex(@"\{([a-z0-9_]+)\}")]
    private static partial Regex Placeholder();
}
