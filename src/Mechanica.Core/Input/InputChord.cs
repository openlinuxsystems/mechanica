namespace Mechanica.Core.Input;

/// <summary>
/// One key or mouse button plus the modifiers held with it, written as text such as <c>Q</c>,
/// <c>Shift+Q</c>, <c>Alt+Z</c>, <c>MouseLeft</c> or <c>Alt+WheelUp</c>. Keys use Godot's
/// <c>Key</c> names (<c>Key1</c>, <c>Space</c>, <c>Escape</c>, <c>Up</c>) so the game can map them
/// without Core knowing Godot. Mouse inputs are <see cref="MouseButtons"/>. A bare modifier key
/// (<c>Shift</c>, <c>Ctrl</c>, <c>Alt</c>) is a key in its own right, as Sprint and Crawl use it.
/// </summary>
public readonly record struct InputChord(string Code, bool Shift = false, bool Ctrl = false, bool Alt = false)
{
    /// <summary>The mouse inputs a chord can name.</summary>
    public static readonly IReadOnlySet<string> MouseButtons = new HashSet<string>(StringComparer.Ordinal)
    {
        "MouseLeft", "MouseRight", "MouseMiddle", "WheelUp", "WheelDown", "MouseX1", "MouseX2",
    };

    private static readonly HashSet<string> ModifierKeys = new(StringComparer.Ordinal) { "Shift", "Ctrl", "Alt" };

    /// <summary>True for a mouse button or wheel.</summary>
    public bool IsMouse => MouseButtons.Contains(Code);

    /// <summary>True for the mouse wheel.</summary>
    public bool IsWheel => Code is "WheelUp" or "WheelDown";

    /// <summary>Parses <c>Shift+Q</c>; false for empty text, an unknown modifier or a doubled one.</summary>
    public static bool TryParse(string? text, out InputChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(p => p.Length == 0))
            return false;
        bool shift = false, ctrl = false, alt = false;
        foreach (var mod in parts[..^1])
        {
            switch (mod)
            {
                case "Shift" when !shift: shift = true; break;
                case "Ctrl" when !ctrl: ctrl = true; break;
                case "Alt" when !alt: alt = true; break;
                default: return false;
            }
        }
        var code = parts[^1];
        if (!code.All(char.IsAsciiLetterOrDigit))
            return false;
        chord = new InputChord(code, shift, ctrl, alt);
        return true;
    }

    /// <summary>Parses <c>Shift+Q</c> or throws.</summary>
    public static InputChord Parse(string text) =>
        TryParse(text, out var chord) ? chord : throw new FormatException($"'{text}' is not a key or mouse binding.");

    /// <summary>The text form, modifiers first in Ctrl, Shift, Alt order.</summary>
    public override string ToString() =>
        $"{(Ctrl ? "Ctrl+" : "")}{(Shift ? "Shift+" : "")}{(Alt ? "Alt+" : "")}{Code}";

    /// <summary>What a player reads: <c>Shift + Q</c>, <c>Left Click</c>, <c>Scroll Up</c>, <c>1</c>.</summary>
    public string Display()
    {
        var name = Code switch
        {
            "MouseLeft" => "Left Click",
            "MouseRight" => "Right Click",
            "MouseMiddle" => "Middle Click",
            "MouseX1" => "Mouse 4",
            "MouseX2" => "Mouse 5",
            "WheelUp" => "Scroll Up",
            "WheelDown" => "Scroll Down",
            "Escape" => "Esc",
            "Up" => "Up Arrow",
            "Down" => "Down Arrow",
            "Left" => "Left Arrow",
            "Right" => "Right Arrow",
            "Pageup" => "Page Up",
            "Pagedown" => "Page Down",
            ['K', 'e', 'y', var d] when char.IsAsciiDigit(d) => d.ToString(),
            _ => Code,
        };
        return string.Join(" + ", new[] { Ctrl ? "Ctrl" : null, Shift ? "Shift" : null, Alt ? "Alt" : null, name }.OfType<string>());
    }

    /// <summary>True when this chord is just a modifier key with no other modifiers (Shift for Sprint).</summary>
    public bool IsBareModifier => ModifierKeys.Contains(Code) && !Shift && !Ctrl && !Alt;
}
