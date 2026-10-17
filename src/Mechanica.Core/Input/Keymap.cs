namespace Mechanica.Core.Input;

/// <summary>A binding slot: each action has a primary and an alternate binding.</summary>
public enum BindingSlot
{
    /// <summary>The first binding.</summary>
    Primary,

    /// <summary>The second binding.</summary>
    Alternate,
}

/// <summary>Another action already on a chord (UI spec §16 "Conflict detection").</summary>
/// <param name="Action">The action that has the chord.</param>
/// <param name="Slot">Which of its bindings.</param>
public readonly record struct BindingConflict(string Action, BindingSlot Slot);

/// <summary>
/// The player's key bindings: the <see cref="InputActions"/> defaults plus their changes. Changes are
/// stored as text (<c>{"rotate": ["Q", "Shift+WheelDown"]}</c>) in the user settings, and an empty string
/// clears a slot. Two actions conflict when they share a chord in overlapping contexts; rebinding onto a
/// conflict can swap the two or take the chord over.
/// </summary>
public sealed class Keymap
{
    private readonly Dictionary<string, InputChord?[]> _bindings = new(StringComparer.Ordinal);

    /// <summary>A keymap with every default.</summary>
    public Keymap() => ResetAll();

    /// <summary>A keymap with the defaults plus <paramref name="overrides"/>; unknown actions and bad chords are skipped.</summary>
    public static Keymap FromOverrides(IReadOnlyDictionary<string, List<string>>? overrides)
    {
        var map = new Keymap();
        foreach (var (action, slots) in overrides ?? new Dictionary<string, List<string>>())
        {
            if (!map._bindings.TryGetValue(action, out var current))
                continue;
            for (var i = 0; i < Math.Min(2, slots.Count); i++)
            {
                if (slots[i].Length == 0)
                    current[i] = null;
                else if (InputChord.TryParse(slots[i], out var chord))
                    current[i] = chord;
            }
        }
        return map;
    }

    /// <summary>What differs from the defaults, for the settings file (sorted by action).</summary>
    public SortedDictionary<string, List<string>> Overrides()
    {
        var result = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var def in InputActions.All)
        {
            var now = _bindings[def.Id];
            if (now[0] != Default(def, BindingSlot.Primary) || now[1] != Default(def, BindingSlot.Alternate))
                result[def.Id] = [now[0]?.ToString() ?? "", now[1]?.ToString() ?? ""];
        }
        return result;
    }

    /// <summary>The binding in <paramref name="slot"/> of <paramref name="action"/>, or null when empty.</summary>
    public InputChord? Get(string action, BindingSlot slot = BindingSlot.Primary) => Slots(action)[(int)slot];

    /// <summary>Both bindings of <paramref name="action"/>, empty ones left out.</summary>
    public IEnumerable<InputChord> Bindings(string action) => Slots(action).OfType<InputChord>();

    /// <summary>The primary binding as a player reads it (the alternate when there is no primary), or "unbound".</summary>
    public string Label(string action) => (Get(action) ?? Get(action, BindingSlot.Alternate))?.Display() ?? "unbound";

    /// <summary>Actions other than <paramref name="action"/> that would clash with it on <paramref name="chord"/>.</summary>
    public IReadOnlyList<BindingConflict> ConflictsWith(string action, InputChord chord)
    {
        var context = Def(action).Context;
        var conflicts = new List<BindingConflict>();
        foreach (var def in InputActions.All)
        {
            if (def.Id == action || !Overlap(context, def.Context))
                continue;
            var slots = _bindings[def.Id];
            for (var i = 0; i < 2; i++)
            {
                if (slots[i] == chord)
                    conflicts.Add(new BindingConflict(def.Id, (BindingSlot)i));
            }
        }
        return conflicts;
    }

    /// <summary>Every action whose bindings clash with another's (for red rows in Controls).</summary>
    public IReadOnlySet<string> Conflicting()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in InputActions.All)
        {
            foreach (var chord in Bindings(def.Id))
            {
                if (ConflictsWith(def.Id, chord).Count > 0)
                    result.Add(def.Id);
            }
        }
        return result;
    }

    /// <summary>
    /// Binds <paramref name="chord"/> to a slot (null clears it). An action that already had the chord in
    /// an overlapping context loses it; with <paramref name="swap"/> it gets this slot's old binding
    /// instead. Returns the conflicts that were resolved.
    /// </summary>
    public IReadOnlyList<BindingConflict> Set(string action, BindingSlot slot, InputChord? chord, bool swap = false)
    {
        var slots = Slots(action);
        var old = slots[(int)slot];
        var conflicts = chord is { } c ? ConflictsWith(action, c) : [];
        foreach (var conflict in conflicts)
            _bindings[conflict.Action][(int)conflict.Slot] = swap ? old : null;
        slots[(int)slot] = chord;
        // Don't keep the same chord in both slots of one action.
        var other = 1 - (int)slot;
        if (chord is not null && slots[other] == chord)
            slots[other] = null;
        return conflicts;
    }

    /// <summary>Puts one action back to its defaults.</summary>
    public void Reset(string action)
    {
        var def = Def(action);
        _bindings[action] = [Default(def, BindingSlot.Primary), Default(def, BindingSlot.Alternate)];
    }

    /// <summary>Puts every action in <paramref name="category"/> back to its defaults.</summary>
    public void ResetCategory(string category)
    {
        foreach (var def in InputActions.All.Where(a => a.Category == category))
            Reset(def.Id);
    }

    /// <summary>Puts every action back to its defaults.</summary>
    public void ResetAll()
    {
        foreach (var def in InputActions.All)
            Reset(def.Id);
    }

    private static bool Overlap(InputContext a, InputContext b) =>
        a == b || a == InputContext.General || b == InputContext.General;

    private static InputChord? Default(InputActionDef def, BindingSlot slot) =>
        (slot == BindingSlot.Primary ? def.Primary : def.Alternate) is { } text ? InputChord.Parse(text) : null;

    private static InputActionDef Def(string action) =>
        InputActions.Find(action) ?? throw new ArgumentException($"'{action}' is not an input action.", nameof(action));

    private InputChord?[] Slots(string action) =>
        _bindings.TryGetValue(action, out var slots) ? slots : throw new ArgumentException($"'{action}' is not an input action.", nameof(action));
}
