using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Creations;

/// <summary>
/// A part's words drawn on its face (<see cref="TextDisplayDefinition"/>): a lit <see cref="Label3D"/> in the area the
/// part's data gives, fitted to it: as large as the area (and its <c>max_height</c>) allows, centred, never past it. The
/// words are the player's own, so they are never translated.
/// </summary>
public static class PartText
{
    // Glyphs are laid out at this size and scaled down by the label's pixel size to fit; big enough to stay sharp close up.
    private const int FontSize = 96;

    /// <summary>The label for <paramref name="text"/> in the part's frame, showing nothing yet.</summary>
    public static Label3D Create(TextDisplayDefinition text)
    {
        var label = new Label3D
        {
            Name = "Text",
            Font = UiFonts.SemiBold,
            FontSize = FontSize,
            OutlineSize = 0,
            Shaded = true,
            DoubleSided = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = HexColor.TryParse(text.Color ?? "#2b2f35", out var c) ? new Color(c.R, c.G, c.B) : Colors.Black,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        var facing = (text.Facing ?? new Vec3(0, 0, 1)).ToGodot();
        // Label3D reads from +Z; turn it to face the part's facing.
        var yaw = Mathf.Atan2(facing.X, facing.Z);
        label.Transform = new Transform3D(new Basis(Vector3.Up, yaw), (text.At ?? Vec3.Zero).ToGodot());
        return label;
    }

    /// <summary>Shows <paramref name="words"/> on <paramref name="label"/>, fitted into the area.</summary>
    public static void Show(Label3D label, TextDisplayDefinition text, SettingDefinition? setting, string words)
    {
        label.Text = words;
        label.Visible = words.Trim().Length > 0;
        if (!label.Visible)
            return;
        var width = text.Size?.ElementAtOrDefault(0) ?? 0.5f;
        var height = text.Size?.ElementAtOrDefault(1) ?? 0.25f;
        var lines = Math.Max(1, words.Split('\n').Length);
        var font = label.Font ?? UiFonts.SemiBold;
        var size = font.GetMultilineStringSize(words, HorizontalAlignment.Center, -1, FontSize);
        var lineHeight = font.GetHeight(FontSize);
        // Metres per pixel: the widest line fits the width, every line fits the height, and no line is taller than
        // max_height (or the height shared out over the setting's lines, so short notes are not drawn huge).
        var tallest = text.MaxHeight ?? height / Math.Max(1, setting?.Lines ?? lines);
        label.PixelSize = Mathf.Min(Mathf.Min(width / Mathf.Max(1f, size.X), height / Mathf.Max(1f, lineHeight * lines)), tallest / lineHeight);
    }

    /// <summary>The words of <paramref name="text"/>'s setting on a part with <paramref name="stored"/> settings.</summary>
    public static string WordsOf(PartDefinition definition, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? stored, TextDisplayDefinition text) =>
        PartSettings.Value(definition, stored, text.Setting ?? "") is { ValueKind: System.Text.Json.JsonValueKind.String } value ? value.GetString() ?? "" : "";
}
