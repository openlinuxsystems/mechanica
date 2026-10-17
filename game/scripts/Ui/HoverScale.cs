using Godot;
using Mechanica.Core.Ui;

namespace Mechanica.Game.Ui;

/// <summary>
/// Grows a button to 1.05× around its centre while the mouse is over it (UI spec §13 "Buttons").
/// <see cref="UiRoot"/> attaches it to every plain button; check boxes and check buttons keep their size,
/// and a button with the <see cref="OptOut"/> meta set is left alone.
/// </summary>
public static class HoverScale
{
    /// <summary>Meta key that keeps a button from scaling.</summary>
    public const string OptOut = "no_hover_scale";

    private const string Attached = "hover_scale";
    private const double Seconds = 0.08;

    /// <summary>Adds the hover scale to <paramref name="button"/> once.</summary>
    public static void Attach(Button button)
    {
        if (button is CheckBox or CheckButton || button.HasMeta(Attached) || button.HasMeta(OptOut))
            return;
        button.SetMeta(Attached, true);
        button.Resized += () => button.PivotOffset = button.Size / 2;
        button.MouseEntered += () => To(button, button.Disabled ? 1f : UiPalette.ButtonHoverScale);
        button.MouseExited += () => To(button, 1f);
    }

    private static void To(Button button, float scale)
    {
        if (!button.IsInsideTree())
            return;
        button.PivotOffset = button.Size / 2;
        button.CreateTween().TweenProperty(button, "scale", new Vector2(scale, scale), Seconds);
    }
}
