using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// The bundled interface fonts (UI spec §0 "Typography"): Barlow for body text and Barlow Condensed for titles, names,
/// section headings and numbers (SIL Open Font License, <c>game/fonts/OFL.txt</c>). Each falls back to Godot's own font
/// for scripts Barlow lacks (CJK, for example).
/// </summary>
public static class UiFonts
{
    private static readonly Dictionary<string, Font> Loaded = [];

    /// <summary>Barlow Regular, for body text.</summary>
    public static Font Regular => Load("Barlow-Regular");

    /// <summary>Barlow SemiBold, for labels and buttons.</summary>
    public static Font SemiBold => Load("Barlow-SemiBold");

    /// <summary>Barlow Bold.</summary>
    public static Font Bold => Load("Barlow-Bold");

    /// <summary>Barlow Condensed Bold, for display titles, headings, sections and numbers.</summary>
    public static Font CondensedBold => Load("BarlowCondensed-Bold");

    /// <summary><paramref name="font"/> with <paramref name="tracking"/> extra pixels between letters.</summary>
    public static Font Tracked(Font font, int tracking) => new FontVariation { BaseFont = font, SpacingGlyph = tracking };

    private static Font Load(string name)
    {
        if (Loaded.TryGetValue(name, out var font))
            return font;
        var path = $"res://fonts/{name}.ttf";
        var file = ResourceLoader.Exists(path) ? GD.Load<FontFile>(path) : null;
        font = file is null ? ThemeDB.FallbackFont : new FontVariation { BaseFont = file, Fallbacks = [ThemeDB.FallbackFont] };
        Loaded[name] = font;
        return font;
    }
}
