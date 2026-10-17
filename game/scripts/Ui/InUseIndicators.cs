using Godot;
using Mechanica.Core;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.Net;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Items;
using Mechanica.Game.Logic;
using Mechanica.Game.Net;

namespace Mechanica.Game.Ui;

/// <summary>
/// "In use" icons (UI spec §11 Multiplayer UI indicators): a small blue person icon with the player's name
/// over every station or container another player has open, every seat another player sits in and every
/// other player's Lift. The host tells every peer what is open (<see cref="InventorySync.OpenByAnyone"/>);
/// seats and Lifts are already synced. Icons stay readable through walls, fade out between
/// <see cref="FadeStart"/> and <see cref="FadeEnd"/>, and hide with Hide GUI.
/// </summary>
public partial class InUseIndicators : Node
{
    /// <summary>Metres from the camera where an icon starts to fade, and where it is gone.</summary>
    public const float FadeStart = 25f, FadeEnd = 45f;

    /// <summary>Metres between the top of a part or Lift and its icon.</summary>
    public const float Clearance = 0.35f;

    private static ImageTexture? _icon;
    private readonly Dictionary<(InUseKind Kind, int Creation, int Part), Mark> _marks = [];

    /// <summary>The session, for this peer's id. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players, for names. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Who has which station or container open. Set before adding to the tree.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Who sits where; null marks no seats.</summary>
    public LogicSync? Logic { get; init; }

    /// <summary>Lifts; null marks no Lifts.</summary>
    public BuildSync? Build { get; init; }

    /// <summary>The Creations shown here. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>The icon drawn over an object in use: a person on a navy disc with a blue ring.</summary>
    public static ImageTexture Icon => _icon ??= DrawIcon();

    /// <summary>The marks shown now, with where each sits and the text under it, for tests.</summary>
    public IEnumerable<(InUseMark Mark, Vector3 Position, string Text)> Shown =>
        _marks.Values.Where(m => m.Icon.Visible).Select(m => (m.Data, m.Icon.GlobalPosition, m.Label.Text));

    public override void _Process(double delta)
    {
        var wanted = InUseMarks.For(Session.LocalPeer, Inventory.OpenByAnyone, Logic?.Seats.All ?? [],
            Build?.Lifts.Keys ?? Enumerable.Empty<int>());
        var keep = new HashSet<(InUseKind, int, int)>();
        var camera = GetViewport().GetCamera3D();
        foreach (var data in wanted)
        {
            var key = (data.Kind, data.Creation, data.Part);
            keep.Add(key);
            if (!_marks.TryGetValue(key, out var mark))
                _marks[key] = mark = new Mark(this);
            mark.Data = data;
            var at = PositionOf(data);
            if (at is null || HudRoot.Hidden)
            {
                mark.Show(false);
                continue;
            }
            mark.Icon.GlobalPosition = at.Value;
            mark.Label.Text = Loc.T("In use: {0}", string.Join(", ", data.Peers.Select(NameOf)));
            var distance = camera is null ? 0f : camera.GlobalPosition.DistanceTo(at.Value);
            var alpha = 1f - Mathf.Clamp((distance - FadeStart) / (FadeEnd - FadeStart), 0f, 1f);
            mark.Icon.Modulate = Colors.White with { A = alpha };
            mark.Label.Modulate = UiTokens.Blue with { A = alpha };
            mark.Label.OutlineModulate = UiTokens.DeepNavy with { A = alpha };
            mark.Show(alpha > 0f);
        }
        foreach (var key in _marks.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _marks[key].Icon.QueueFree();
            _marks.Remove(key);
        }
    }

    private string NameOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) && player.DisplayName.Length > 0 ? player.DisplayName : Loc.T("Player {0}", peer);

    // Over the middle of the part or the Lift's platform, or null when it is not shown here.
    private Vector3? PositionOf(InUseMark mark)
    {
        if (mark.Kind == InUseKind.Lift)
            return Build?.Lifts.GetValueOrDefault(mark.Part) is { } lift && IsInstanceValid(lift) && lift.IsInsideTree()
                ? lift.Top + new Vector3(0, Clearance + 0.25f, 0)
                : null;
        if (Creations.NodeOf(mark.Creation) is not { } node || Creations.Find(mark.Creation) is not { } creation
            || creation.FindPart(mark.Part) is not { } part || ConnectionNodes.Centre(node, mark.Part) is not { } centre)
            return null;
        var size = creation.DefinitionOf(part.Id).Size ?? GridSize.One;
        var reach = Math.Max(size.X, Math.Max(size.Y, size.Z)) * MechanicaInfo.GridSize / 2;
        return centre + new Vector3(0, reach + Clearance, 0);
    }

    private static ImageTexture DrawIcon()
    {
        const int size = 64;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var middle = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2(x, y);
                var r = p.DistanceTo(middle);
                Color colour;
                if (r > 31f)
                    colour = Colors.Transparent;
                else if (r > 27f)
                    colour = UiTokens.Blue;
                else if (p.DistanceTo(new Vector2(middle.X, 23f)) < 8.5f
                    || (y > 34 && y < 50 && new Vector2((x - middle.X) / 15f, (y - 50f) / 14f).LengthSquared() < 1f))
                    colour = UiTokens.Cyan;
                else
                    colour = UiTokens.Navy;
                image.SetPixel(x, y, colour);
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    // One icon and its name label.
    private sealed class Mark
    {
        public Mark(Node owner)
        {
            // Both are fixed-size, so the label hangs under the icon by a pixel offset, not a distance.
            Icon = new Sprite3D
            {
                Name = "InUse",
                Texture = InUseIndicators.Icon,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FixedSize = true,
                PixelSize = 0.0006f,
                Shaded = false,
                RenderPriority = 1,
            };
            Label = new Label3D
            {
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
                Name = "Text",
                Offset = new Vector2(0, -20),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FixedSize = true,
                PixelSize = 0.0012f,
                FontSize = 22,
                OutlineSize = 8,
                VerticalAlignment = VerticalAlignment.Top,
                RenderPriority = 1,
            };
            Icon.AddChild(Label);
            owner.AddChild(Icon);
        }

        public InUseMark Data { get; set; } = null!;

        public Sprite3D Icon { get; }

        public Label3D Label { get; }

        public void Show(bool visible) => Icon.Visible = visible;
    }
}
