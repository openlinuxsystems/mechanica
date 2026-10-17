using Godot;
using Mechanica.Game.Net;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Ui;

/// <summary>
/// Other players' names over their heads (UI spec §16 Interface, "Show player names"). Each player but
/// this peer's own gets a billboard label that stays readable through walls and faces the camera; the
/// Interface setting hides them all.
/// </summary>
public partial class Nameplates : Node
{
    /// <summary>Metres above a player's feet the name sits.</summary>
    public const float Height = 2.15f;

    private readonly Dictionary<int, Label3D> _plates = [];

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The name label over a player, or null (this peer's own player, or names off).</summary>
    public Label3D? PlateOf(int peer) => _plates.GetValueOrDefault(peer) is { } plate && IsInstanceValid(plate) && plate.Visible ? plate : null;

    public override void _Process(double delta)
    {
        var show = GameSettings.Current.Interface.ShowPlayerNames;
        foreach (var (peer, player) in Players.Players)
        {
            if (player == Players.LocalPlayer)
                continue;
            if (!_plates.TryGetValue(peer, out var plate) || !IsInstanceValid(plate) || plate.GetParent() != player)
            {
                plate = new Label3D
                {
                    Name = "Nameplate",
                    Position = new Vector3(0, Height, 0),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    FixedSize = true,
                    PixelSize = 0.0015f,
                    FontSize = 28,
                    OutlineSize = 8,
                    Modulate = UiTokens.Text,
                    OutlineModulate = UiTokens.DeepNavy,
                };
                player.AddChild(plate);
                _plates[peer] = plate;
            }
            plate.Text = player.DisplayName;
            plate.Visible = show;
        }
        foreach (var peer in _plates.Keys.Where(p => !Players.Players.ContainsKey(p)).ToList())
            _plates.Remove(peer);
    }
}
