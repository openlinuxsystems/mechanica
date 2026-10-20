using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// The design language theme (UI spec §0): opaque panels, a light <c>Surface</c> for what the player browses, dark
/// <c>Chrome</c> headers and <c>Card</c>s for what they inspect and act on, filled buttons, Barlow type and few
/// borders. It goes on the root of a screen built in the new style (the Fabricator, the hotbar) and covers that subtree;
/// the rest of the game keeps <see cref="MechanicaTheme"/> until each screen moves over.
/// <para>Panel variations (PanelContainer): <c>MainWindow</c>, <c>Header</c>, <c>TabStrip</c>, <c>SurfacePanel</c>,
/// <c>Drawer</c>, <c>QueueBand</c>, <c>CardPanel</c>, <c>InsetPanel</c>, <c>Pill</c>, <c>WarningPill</c>, <c>KeyCap</c>,
/// <c>MenuCard</c> and <c>MenuBand</c> (a part's compact menu).
/// Labels: <c>DisplayLabel</c>, <c>HeadingLabel</c>, <c>SectionLabel</c>, <c>InkSectionLabel</c>, <c>MutedLabel</c>,
/// <c>InkLabel</c>, <c>NumericLabel</c>, <c>InkNumericLabel</c>, <c>ErrorTextLabel</c>, <c>SuccessTextLabel</c>,
/// <c>PillLabel</c>, <c>KeyCapLabel</c>. Buttons: the plain button (secondary, on chrome), <c>PrimaryButton</c>,
/// <c>StationTab</c>, <c>RecipeTile</c>, <c>StepperButton</c>, <c>CloseButton</c>. Line edits: the plain one (a
/// field on chrome) and <c>StepperField</c>.</para>
/// </summary>
public static class StationTheme
{
    /// <summary>Corner radius of windows and cards.</summary>
    public const int WindowCorner = 14;

    /// <summary>Corner radius of inner panels and tiles.</summary>
    public const int TileCorner = 8;

    /// <summary>Corner radius of buttons.</summary>
    public const int ButtonCorner = 10;

    /// <summary>Width of the white ring round a selected tile.</summary>
    public const int Ring = 3;

    private static Theme? _theme;

    /// <summary>The shared theme, built once.</summary>
    public static Theme Theme => _theme ??= Build();

    /// <summary>A flat box: <paramref name="fill"/>, corners, padding and an optional darker bottom edge.</summary>
    public static StyleBoxFlat Box(Color fill, int corner = TileCorner, float margin = 0, int edge = 0)
    {
        var box = new StyleBoxFlat { BgColor = fill, AntiAliasing = true, CornerDetail = 6 };
        box.SetCornerRadiusAll(corner);
        box.SetContentMarginAll(margin);
        if (edge > 0)
        {
            // Tiles and buttons sit on a slightly darker lip (§0 "Spacing, shape and depth").
            box.BorderWidthBottom = edge;
            box.BorderColor = fill.Darkened(0.14f);
        }
        return box;
    }

    /// <summary>A tile's box in each state: normal, hovered, or selected (lighter, with the white ring and a soft shadow).</summary>
    public static StyleBoxFlat TileBox(bool selected = false, bool hovered = false)
    {
        var key = selected ? 2 : hovered ? 1 : 0;
        return TileBoxes[key] ??= MakeTileBox(selected, hovered);
    }

    private static readonly StyleBoxFlat?[] TileBoxes = new StyleBoxFlat?[3];

    private static StyleBoxFlat MakeTileBox(bool selected, bool hovered)
    {
        if (!selected)
            return Box(hovered ? UiTokens.TileHover : UiTokens.Tile, edge: 3);
        var box = Box(UiTokens.TileSelected);
        box.SetBorderWidthAll(Ring);
        box.BorderColor = Colors.White;
        box.ShadowColor = UiTokens.Ink with { A = 0.35f };
        box.ShadowSize = 6;
        box.ShadowOffset = new Vector2(0, 3);
        return box;
    }

    /// <summary>Builds a fresh theme.</summary>
    public static Theme Build()
    {
        var t = new Theme { DefaultFont = UiFonts.Regular, DefaultFontSize = 15 };
        Panels(t);
        Labels(t);
        Buttons(t);
        Fields(t);
        Scrollbars(t);
        t.SetStylebox("panel", "TooltipPanel", Box(UiTokens.Card, TileCorner, 8));
        t.SetColor("font_color", "TooltipLabel", UiTokens.Text);
        return t;
    }

    private static void Panels(Theme t)
    {
        // No drop shadows on windows: over the world they read as a pale translucent border.
        var window = Box(UiTokens.Surface, WindowCorner);
        var card = Box(UiTokens.Card, WindowCorner, 16);
        var header = Box(UiTokens.Chrome, WindowCorner);
        (header.CornerRadiusBottomLeft, header.CornerRadiusBottomRight) = (0, 0);
        (header.ContentMarginLeft, header.ContentMarginRight, header.ContentMarginTop, header.ContentMarginBottom) = (16, 16, 12, 8);
        var tabs = Box(UiTokens.Chrome, 0);
        (tabs.ContentMarginLeft, tabs.ContentMarginRight, tabs.ContentMarginTop) = (16, 12, 2);
        var queue = Box(UiTokens.SurfaceDeep, WindowCorner, 0);
        (queue.CornerRadiusTopLeft, queue.CornerRadiusTopRight) = (0, 0);
        (queue.ContentMarginLeft, queue.ContentMarginRight, queue.ContentMarginTop, queue.ContentMarginBottom) = (16, 16, 8, 12);
        var drawer = Box(UiTokens.SurfaceDeep, 0, 12);
        var surface = Box(UiTokens.Surface, 0);
        (surface.ContentMarginLeft, surface.ContentMarginRight, surface.ContentMarginTop, surface.ContentMarginBottom) = (16, 8, 14, 10);
        var pill = Box(UiTokens.Accent, 12);
        (pill.ContentMarginLeft, pill.ContentMarginRight, pill.ContentMarginTop, pill.ContentMarginBottom) = (10, 10, 2, 2);
        var warning = Box(UiTokens.Warning, 12);
        (warning.ContentMarginLeft, warning.ContentMarginRight, warning.ContentMarginTop, warning.ContentMarginBottom) = (10, 10, 2, 2);
        // A part's compact menu (an engine's lever menu): a Card window, its header and bottom band in Chrome.
        var menuCard = Box(UiTokens.Card, WindowCorner);
        var menuBand = Box(UiTokens.Chrome, 0);
        (menuBand.ContentMarginLeft, menuBand.ContentMarginRight, menuBand.ContentMarginTop, menuBand.ContentMarginBottom) = (20, 20, 14, 14);
        var key = Box(Colors.White with { A = 0.12f }, 4);
        (key.ContentMarginLeft, key.ContentMarginRight) = (6, 6);
        foreach (var (name, box) in new (string, StyleBox)[]
        {
            ("MainWindow", window), ("Header", header), ("TabStrip", tabs), ("SurfacePanel", surface), ("Drawer", drawer),
            ("QueueBand", queue), ("CardPanel", card), ("InsetPanel", Box(UiTokens.Inset, TileCorner, 8)), ("Pill", pill),
            ("WarningPill", warning), ("KeyCap", key), ("MenuCard", menuCard), ("MenuBand", menuBand),
        })
        {
            t.SetTypeVariation(name, "PanelContainer");
            t.SetStylebox("panel", name, box);
        }
        t.SetStylebox("panel", "PanelContainer", new StyleBoxEmpty());
    }

    private static void Labels(Theme t)
    {
        t.SetColor("font_color", "Label", UiTokens.Text);
        var display = UiFonts.Tracked(UiFonts.CondensedBold, 3);
        var section = UiFonts.Tracked(UiFonts.CondensedBold, 2);
        foreach (var (name, font, size, color) in new (string, Font, int, Color)[]
        {
            ("DisplayLabel", display, 30, UiTokens.Text),
            ("HeadingLabel", UiFonts.CondensedBold, 24, UiTokens.Text),
            ("SectionLabel", section, 13, UiTokens.TextMuted),
            ("InkSectionLabel", section, 13, UiTokens.Ink),
            ("MutedLabel", UiFonts.Regular, 13, UiTokens.TextMuted),
            ("InkLabel", UiFonts.SemiBold, 13, UiTokens.Ink),
            ("NumericLabel", UiFonts.CondensedBold, 15, UiTokens.Text),
            ("InkNumericLabel", UiFonts.CondensedBold, 15, UiTokens.Ink),
            ("ErrorTextLabel", UiFonts.Regular, 13, UiTokens.ErrorText),
            ("SuccessTextLabel", UiFonts.Regular, 13, UiTokens.SuccessText),
            ("PillLabel", section, 13, UiTokens.AccentInk),
            ("KeyCapLabel", UiFonts.CondensedBold, 12, UiTokens.TextMuted),
        })
        {
            t.SetTypeVariation(name, "Label");
            t.SetFont("font", name, font);
            t.SetFontSize("font_size", name, size);
            t.SetColor("font_color", name, color);
        }
    }

    private static void Buttons(Theme t)
    {
        // The plain button is the secondary one on chrome: Chrome2 fill, white text, a hairline.
        ButtonType(t, "Button", UiTokens.Chrome2, UiTokens.Text, UiTokens.Text, UiFonts.SemiBold, 14, hairline: true);
        t.SetTypeVariation("PrimaryButton", "Button");
        ButtonType(t, "PrimaryButton", UiTokens.Accent, UiTokens.AccentInk, UiTokens.DisabledText, UiFonts.Tracked(UiFonts.CondensedBold, 1), 22, edge: 4);
        t.SetStylebox("disabled", "PrimaryButton", Box(UiTokens.DisabledFill, ButtonCorner, 6));
        t.SetTypeVariation("CloseButton", "Button");
        ButtonType(t, "CloseButton", UiTokens.Chrome2, UiTokens.TextMuted, UiTokens.Disabled, UiFonts.SemiBold, 16, hairline: true);
        t.SetTypeVariation("StepperButton", "Button");
        ButtonType(t, "StepperButton", UiTokens.Inset, UiTokens.TextMuted, UiTokens.Disabled, UiFonts.SemiBold, 18);

        t.SetTypeVariation("StationTab", "Button");
        var tab = Box(UiTokens.Chrome2, ButtonCorner);
        (tab.CornerRadiusBottomLeft, tab.CornerRadiusBottomRight) = (0, 0);
        (tab.ContentMarginLeft, tab.ContentMarginRight, tab.ContentMarginTop, tab.ContentMarginBottom) = (15, 15, 6, 6);
        var tabHover = (StyleBoxFlat)tab.Duplicate();
        tabHover.BgColor = UiTokens.Chrome2.Lightened(0.08f);
        var tabOn = (StyleBoxFlat)tab.Duplicate();
        tabOn.BgColor = UiTokens.Surface;
        foreach (var (state, box) in new[] { ("normal", tab), ("hover", tabHover), ("pressed", tabOn), ("hover_pressed", tabOn), ("disabled", tab) })
            t.SetStylebox(state, "StationTab", box);
        t.SetStylebox("focus", "StationTab", new StyleBoxEmpty());
        t.SetFont("font", "StationTab", UiFonts.SemiBold);
        t.SetFontSize("font_size", "StationTab", 15);
        foreach (var (name, color) in new[] { ("font_color", UiTokens.TextMuted), ("font_hover_color", UiTokens.Text), ("font_pressed_color", UiTokens.Ink), ("font_hover_pressed_color", UiTokens.Ink), ("font_focus_color", UiTokens.TextMuted) })
            t.SetColor(name, "StationTab", color);

        t.SetTypeVariation("RecipeTile", "Button");
        foreach (var (state, box) in new[]
        {
            ("normal", TileBox()), ("hover", TileBox(hovered: true)), ("pressed", TileBox(true)), ("hover_pressed", TileBox(true)), ("disabled", TileBox()),
        })
            t.SetStylebox(state, "RecipeTile", box);
        t.SetStylebox("focus", "RecipeTile", new StyleBoxEmpty());
    }

    private static void ButtonType(Theme t, string type, Color fill, Color text, Color disabledText, Font font, int size, bool hairline = false, int edge = 0)
    {
        StyleBoxFlat Make(Color c)
        {
            var box = Box(c, ButtonCorner, 6, edge);
            (box.ContentMarginLeft, box.ContentMarginRight) = (12, 12);
            if (hairline)
            {
                box.SetBorderWidthAll(1);
                box.BorderColor = UiTokens.ChromeLine;
            }
            return box;
        }
        // Hover is one step lighter, pressed one step darker (§0).
        t.SetStylebox("normal", type, Make(fill));
        t.SetStylebox("hover", type, Make(fill.Lightened(0.1f)));
        t.SetStylebox("pressed", type, Make(fill.Darkened(0.12f)));
        t.SetStylebox("hover_pressed", type, Make(fill.Darkened(0.12f)));
        t.SetStylebox("disabled", type, Make(fill with { A = 0.6f }));
        var focus = Box(Colors.Transparent, ButtonCorner, 6);
        focus.SetBorderWidthAll(2);
        focus.BorderColor = UiTokens.Accent;
        t.SetStylebox("focus", type, focus);
        t.SetFont("font", type, font);
        t.SetFontSize("font_size", type, size);
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color", "icon_normal_color", "icon_hover_color" })
            t.SetColor(name, type, text);
        t.SetColor("font_disabled_color", type, disabledText);
    }

    private static void Fields(Theme t)
    {
        var field = Box(UiTokens.Chrome2, TileCorner, 6);
        field.SetBorderWidthAll(1);
        field.BorderColor = UiTokens.ChromeLine;
        (field.ContentMarginLeft, field.ContentMarginRight) = (10, 10);
        var focus = (StyleBoxFlat)field.Duplicate();
        focus.BorderColor = UiTokens.Accent;
        t.SetStylebox("normal", "LineEdit", field);
        t.SetStylebox("focus", "LineEdit", focus);
        t.SetStylebox("read_only", "LineEdit", field);
        t.SetFont("font", "LineEdit", UiFonts.Regular);
        t.SetFontSize("font_size", "LineEdit", 14);
        t.SetColor("font_color", "LineEdit", UiTokens.Text);
        t.SetColor("font_placeholder_color", "LineEdit", UiTokens.TextMuted);
        t.SetColor("caret_color", "LineEdit", UiTokens.Accent);
        t.SetColor("selection_color", "LineEdit", UiTokens.Accent with { A = 0.35f });
        t.SetColor("clear_button_color", "LineEdit", UiTokens.TextMuted);

        t.SetTypeVariation("StepperField", "LineEdit");
        foreach (var state in new[] { "normal", "focus", "read_only" })
            t.SetStylebox(state, "StepperField", new StyleBoxEmpty());
        t.SetFont("font", "StepperField", UiFonts.CondensedBold);
        t.SetFontSize("font_size", "StepperField", 20);
    }

    private static void Scrollbars(Theme t)
    {
        // 8 px and rounded: white on the surface (§0 "Scrollbar").
        foreach (var type in new[] { "HScrollBar", "VScrollBar" })
        {
            var track = Box(UiTokens.Ink with { A = 0.18f }, 4, 0);
            (track.ContentMarginLeft, track.ContentMarginRight, track.ContentMarginTop, track.ContentMarginBottom) = (4, 4, 4, 4);
            t.SetStylebox("scroll", type, track);
            t.SetStylebox("scroll_focus", type, track);
            foreach (var (state, alpha) in new[] { ("grabber", 0.8f), ("grabber_highlight", 1f), ("grabber_pressed", 1f) })
                t.SetStylebox(state, type, Box(Colors.White with { A = alpha }, 4));
        }
    }
}
