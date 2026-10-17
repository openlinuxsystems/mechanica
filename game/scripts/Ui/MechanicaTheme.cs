using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// Builds the one theme every screen inherits (UI spec §13): dark navy panels at 85% opacity, blue
/// borders, cyan hover, focus, selection, progress and thin scrollbars, white and light-grey text.
/// <see cref="UiRoot"/> puts it on the root window, so menus, the HUD and every mod tool pick it up
/// without styling themselves. Types the default theme styles differently from a plain button (check
/// boxes, check buttons) get their own entries so they don't inherit button boxes.
/// <para>Type variations: <c>PrimaryButton</c> (cyan, for the main action), <c>DangerButton</c> (for
/// destructive confirms), <c>TitleBar</c>, <c>TitleLabel</c>, <c>SecondaryLabel</c>, <c>ErrorLabel</c>,
/// <c>WarningLabel</c> and <c>SuccessLabel</c>.</para>
/// </summary>
public static class MechanicaTheme
{
    private const int Border = 2;
    private const int Corner = 4;

    private static Theme? _theme;

    /// <summary>The shared theme, built once.</summary>
    public static Theme Theme => _theme ??= Build();

    /// <summary>Builds a fresh theme.</summary>
    public static Theme Build()
    {
        var t = new Theme();
        Panels(t);
        Text(t);
        Buttons(t);
        Inputs(t);
        Lists(t);
        Tabs(t);
        Bars(t);
        Popups(t);
        return t;
    }

    /// <summary>A flat box with the theme's corners.</summary>
    public static StyleBoxFlat Box(Color fill, Color? border = null, int borderWidth = Border, float margin = 8)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border ?? fill, AntiAliasing = true };
        box.SetBorderWidthAll(border is null ? 0 : borderWidth);
        box.SetCornerRadiusAll(Corner);
        box.SetContentMarginAll(margin);
        return box;
    }

    /// <summary>The bright cyan border a selected slot or item gets (§3, §8).</summary>
    public static StyleBoxFlat SelectedSlot() => Box(UiTokens.ControlFill, UiTokens.Cyan, 3, 4);

    private static void Panels(Theme t)
    {
        var panel = Box(UiTokens.Panel, UiTokens.Blue, margin: 12);
        t.SetStylebox("panel", "Panel", panel);
        t.SetStylebox("panel", "PanelContainer", panel);
        t.SetTypeVariation("TitleBar", "PanelContainer");
        var title = Box(UiTokens.Blue, margin: 6);
        (title.CornerRadiusBottomLeft, title.CornerRadiusBottomRight) = (0, 0);
        t.SetStylebox("panel", "TitleBar", title);
        t.SetStylebox("panel", "TooltipPanel", Box(UiTokens.Panel, UiTokens.Blue, 1, 6));
        t.SetColor("font_color", "TooltipLabel", UiTokens.Text);
        t.SetStylebox("embedded_border", "Window", Box(UiTokens.Panel, UiTokens.Blue, margin: 0));
        t.SetStylebox("embedded_unfocused_border", "Window", Box(UiTokens.Panel, UiTokens.Disabled, margin: 0));
        t.SetColor("title_color", "Window", UiTokens.Text);
        t.SetStylebox("panel", "AcceptDialog", Box(UiTokens.Panel, margin: 12));
        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = UiTokens.Blue with { A = 0.5f }, Thickness = 1 });
        t.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = UiTokens.Blue with { A = 0.5f }, Thickness = 1, Vertical = true });
    }

    private static void Text(Theme t)
    {
        t.SetColor("font_color", "Label", UiTokens.Text);
        t.SetColor("default_color", "RichTextLabel", UiTokens.Text);
        foreach (var (name, color) in new[]
        {
            ("SecondaryLabel", UiTokens.TextSecondary), ("ErrorLabel", UiTokens.Error),
            ("WarningLabel", UiTokens.Warning), ("SuccessLabel", UiTokens.Success), ("TitleLabel", UiTokens.Text),
        })
        {
            t.SetTypeVariation(name, "Label");
            t.SetColor("font_color", name, color);
        }
        t.SetFontSize("font_size", "TitleLabel", 18);
    }

    private static void Buttons(Theme t)
    {
        ButtonType(t, "Button", UiTokens.Blue, UiTokens.Text);
        t.SetTypeVariation("PrimaryButton", "Button");
        ButtonType(t, "PrimaryButton", UiTokens.Cyan, UiTokens.Cyan);
        t.SetTypeVariation("DangerButton", "Button");
        ButtonType(t, "DangerButton", UiTokens.Error, UiTokens.Text);
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            var empty = new StyleBoxEmpty();
            empty.SetContentMarginAll(4);
            foreach (var state in new[] { "normal", "pressed", "hover", "hover_pressed", "disabled" })
                t.SetStylebox(state, type, empty);
            t.SetStylebox("focus", type, Box(Colors.Transparent, UiTokens.Cyan, 1, 4));
            t.SetColor("font_color", type, UiTokens.Text);
            t.SetColor("font_hover_color", type, UiTokens.Cyan);
            t.SetColor("font_pressed_color", type, UiTokens.Text);
            t.SetColor("font_hover_pressed_color", type, UiTokens.Cyan);
            t.SetColor("font_disabled_color", type, UiTokens.Disabled);
        }
        t.SetColor("font_color", "LinkButton", UiTokens.Cyan);
        t.SetColor("font_hover_color", "LinkButton", UiTokens.Text);
    }

    private static void ButtonType(Theme t, string type, Color border, Color text)
    {
        t.SetStylebox("normal", type, Box(UiTokens.ControlFill, border, margin: 6));
        t.SetStylebox("hover", type, Box(UiTokens.ControlFill, UiTokens.Cyan, margin: 6));
        t.SetStylebox("pressed", type, Box(UiTokens.ControlPressed, UiTokens.Cyan, margin: 6));
        t.SetStylebox("hover_pressed", type, Box(UiTokens.ControlPressed, UiTokens.Cyan, margin: 6));
        t.SetStylebox("disabled", type, Box(UiTokens.ControlFill with { A = 0.6f }, UiTokens.Disabled, margin: 6));
        t.SetStylebox("focus", type, Box(Colors.Transparent, UiTokens.Cyan, 1, 6));
        t.SetColor("font_color", type, text);
        t.SetColor("font_hover_color", type, UiTokens.Cyan);
        t.SetColor("font_pressed_color", type, UiTokens.Text);
        t.SetColor("font_hover_pressed_color", type, UiTokens.Cyan);
        t.SetColor("font_focus_color", type, text);
        t.SetColor("font_disabled_color", type, UiTokens.Disabled);
        t.SetColor("icon_normal_color", type, text);
        t.SetColor("icon_hover_color", type, UiTokens.Cyan);
    }

    private static void Inputs(Theme t)
    {
        foreach (var type in new[] { "LineEdit", "TextEdit" })
        {
            t.SetStylebox("normal", type, Box(UiTokens.ControlFill, UiTokens.Blue, 1, 6));
            t.SetStylebox("focus", type, Box(Colors.Transparent, UiTokens.Cyan, 1, 6));
            t.SetStylebox("read_only", type, Box(UiTokens.ControlFill with { A = 0.6f }, UiTokens.Disabled, 1, 6));
            t.SetColor("font_color", type, UiTokens.Text);
            t.SetColor("font_placeholder_color", type, UiTokens.TextSecondary with { A = 0.7f });
            t.SetColor("caret_color", type, UiTokens.Cyan);
            t.SetColor("selection_color", type, UiTokens.Blue with { A = 0.5f });
        }
        t.SetColor("background_color", "CodeEdit", UiTokens.ControlFill);
        t.SetColor("current_line_color", "CodeEdit", UiTokens.Blue with { A = 0.12f });
        t.SetColor("line_number_color", "CodeEdit", UiTokens.TextSecondary with { A = 0.6f });
        foreach (var type in new[] { "HSlider", "VSlider" })
        {
            t.SetStylebox("slider", type, Box(UiTokens.ControlFill, UiTokens.Blue, 1, 2));
            t.SetStylebox("grabber_area", type, Box(UiTokens.Cyan, margin: 2));
            t.SetStylebox("grabber_area_highlight", type, Box(UiTokens.Cyan, margin: 2));
        }
    }

    private static void Lists(Theme t)
    {
        var selected = Box(UiTokens.Blue with { A = 0.35f }, UiTokens.Cyan, 1, 2);
        var hover = Box(UiTokens.Blue with { A = 0.2f }, margin: 2);
        foreach (var type in new[] { "ItemList", "Tree" })
        {
            t.SetStylebox("panel", type, Box(UiTokens.ControlFill, UiTokens.Blue, 1, 4));
            t.SetStylebox("focus", type, Box(Colors.Transparent, UiTokens.Cyan, 1, 4));
            t.SetStylebox("selected", type, selected);
            t.SetStylebox("selected_focus", type, selected);
            t.SetStylebox("hovered", type, hover);
            t.SetColor("font_color", type, UiTokens.Text);
            t.SetColor("font_selected_color", type, UiTokens.Text);
            t.SetColor("guide_color", type, UiTokens.Blue with { A = 0.25f });
        }
        // Thin cyan scrollbars (§13 "Layout Rules").
        foreach (var type in new[] { "HScrollBar", "VScrollBar" })
        {
            var track = new StyleBoxFlat { BgColor = UiTokens.ControlFill with { A = 0.6f } };
            track.SetCornerRadiusAll(3);
            track.SetContentMarginAll(3);
            t.SetStylebox("scroll", type, track);
            t.SetStylebox("scroll_focus", type, track);
            foreach (var (state, alpha) in new[] { ("grabber", 0.7f), ("grabber_highlight", 1f), ("grabber_pressed", 1f) })
            {
                var grab = new StyleBoxFlat { BgColor = UiTokens.Cyan with { A = alpha } };
                grab.SetCornerRadiusAll(3);
                t.SetStylebox(state, type, grab);
            }
        }
    }

    private static void Tabs(Theme t)
    {
        var selected = Box(UiTokens.ControlFill, UiTokens.Cyan, margin: 8);
        (selected.BorderWidthLeft, selected.BorderWidthRight, selected.BorderWidthTop) = (0, 0, 0);
        selected.BorderWidthBottom = 3;
        var unselected = Box(UiTokens.ControlPressed, margin: 8);
        var hovered = Box(UiTokens.ControlFill, UiTokens.Blue, margin: 8);
        (hovered.BorderWidthLeft, hovered.BorderWidthRight, hovered.BorderWidthTop) = (0, 0, 0);
        foreach (var type in new[] { "TabContainer", "TabBar" })
        {
            t.SetStylebox("tab_selected", type, selected);
            t.SetStylebox("tab_unselected", type, unselected);
            t.SetStylebox("tab_hovered", type, hovered);
            t.SetStylebox("tab_disabled", type, unselected);
            t.SetColor("font_selected_color", type, UiTokens.Cyan);
            t.SetColor("font_unselected_color", type, UiTokens.TextSecondary);
            t.SetColor("font_hovered_color", type, UiTokens.Text);
        }
        t.SetStylebox("panel", "TabContainer", Box(UiTokens.Panel, UiTokens.Blue, 1, 8));
    }

    private static void Bars(Theme t)
    {
        t.SetStylebox("background", "ProgressBar", Box(UiTokens.ControlFill, UiTokens.Blue, 1, 0));
        t.SetStylebox("fill", "ProgressBar", Box(UiTokens.Cyan, margin: 0));
        t.SetColor("font_color", "ProgressBar", UiTokens.Text);
    }

    private static void Popups(Theme t)
    {
        t.SetStylebox("panel", "PopupMenu", Box(UiTokens.Navy, UiTokens.Blue, 1, 4));
        t.SetStylebox("hover", "PopupMenu", Box(UiTokens.Blue with { A = 0.35f }, margin: 4));
        t.SetColor("font_color", "PopupMenu", UiTokens.Text);
        t.SetColor("font_hover_color", "PopupMenu", UiTokens.Cyan);
        t.SetColor("font_disabled_color", "PopupMenu", UiTokens.Disabled);
        t.SetStylebox("panel", "PopupPanel", Box(UiTokens.Navy, UiTokens.Blue, 1, 4));
    }
}
