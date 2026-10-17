using Godot;
using Mechanica.Core.Bots;
using Mechanica.Game.Ui;

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
            ThemeTypeVariation = "ErrorLabel",
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
            RaidPhases.Warning => Loc.T("Raid incoming! Level {0}: {1} bots are coming for your crops.", raid.Level, raid.Bots),
            RaidPhases.Attacking => Loc.T(raid.Bots == 1 ? "Raid level {0}: {1} raider left." : "Raid level {0}: {1} raiders left.", raid.Level, raid.Bots),
            _ => "",
        };
    }
}
