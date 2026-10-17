using Godot;
using Mechanica.Core;

namespace Mechanica.Game.Ui;

/// <summary>The game's title as a logo (UI spec §14–15): white letters with a blue outline and a soft cyan glow.</summary>
public static class Logo
{
    /// <summary>A new logo label <paramref name="fontSize"/> pixels tall: 84 on the home screen, smaller on loading screens.</summary>
    public static Label Create(int fontSize)
    {
        var logo = new Label
        {
            Name = "Logo",
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            Text = MechanicaInfo.GameName.ToUpperInvariant(),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var scale = fontSize / 84f;
        logo.AddThemeFontSizeOverride("font_size", fontSize);
        logo.AddThemeColorOverride("font_color", UiTokens.Text);
        logo.AddThemeColorOverride("font_outline_color", UiTokens.Blue);
        logo.AddThemeConstantOverride("outline_size", Mathf.Max(2, (int)(8 * scale)));
        logo.AddThemeColorOverride("font_shadow_color", UiTokens.Cyan with { A = 0.45f });
        logo.AddThemeConstantOverride("shadow_outline_size", Mathf.Max(6, (int)(22 * scale)));
        logo.AddThemeConstantOverride("shadow_offset_x", 0);
        logo.AddThemeConstantOverride("shadow_offset_y", 0);
        return logo;
    }
}
