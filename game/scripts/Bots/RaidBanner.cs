using Godot;
using Mechanica.Core.Bots;

namespace Mechanica.Game.Bots;

/// <summary>The raid banner at the top of the screen: the horn's warning, then how many raiders are left.</summary>
public partial class RaidBanner : CanvasLayer
{
    private Label _text = null!;

    /// <summary>Bots and raids. Set before adding to the tree.</summary>
    public BotSync Bots { get; init; } = null!;

    public override void _Ready()
    {
        _text = new Label
        {
            Name = "Text",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1f, 0.55f, 0.45f),
            Visible = false,
        };
        _text.AddThemeFontSizeOverride("font_size", 26);
        AddChild(_text);
        _text.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.KeepSize, 24);
    }

    public override void _Process(double delta)
    {
        var raid = Bots.Raid;
        _text.Visible = raid.Phase != RaidPhases.None;
        _text.Text = raid.Phase switch
        {
            RaidPhases.Warning => $"Raid incoming! Level {raid.Level}: {raid.Bots} bots are coming for your crops.",
            RaidPhases.Attacking => $"Raid level {raid.Level}: {raid.Bots} raider{(raid.Bots == 1 ? "" : "s")} left.",
            _ => "",
        };
    }
}
