using System.Threading.Tasks;
using Godot;
using Mechanica.Core.Ui;
using Mechanica.Core.World;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Quests;

/// <summary>
/// The minimap: a north-up square of the map round the player, drawn in the Logbook map's blues at
/// <see cref="LogbookSettings.MinimapMetresPerPixel"/>, with the player's arrow in the middle, the current missions'
/// goals pulsing blue and the player's own pins with their labels. A pin out of range waits on the rim, pointing the
/// way. The picture covers twice the view and is drawn again in the background once the player has walked an
/// eighth of it (<see cref="Minimap"/>). It never takes the mouse.
/// </summary>
public partial class MinimapView : Control
{
    /// <summary>Pixels a pin on the rim keeps from the edge.</summary>
    public const float RimInset = 9f;

    private ImageTexture? _texture;
    private (System.Numerics.Vector2 Min, float Metres)? _drawn;
    private (System.Numerics.Vector2 Min, float Metres, int Pixels) _asked;
    private Task<byte[]>? _job;
    private WorldGenerator? _world;
    private Font _font = null!;
    private System.Numerics.Vector2 _centre;
    private List<(Waypoint Waypoint, LogbookMarkKind Kind, Vector2 At, bool AtEdge)> _shown = [];

    /// <summary>Map numbers. Set before adding to the tree.</summary>
    public LogbookSettings Settings { get; init; } = new();

    /// <summary>The world to draw, or null before it exists. Set before adding to the tree.</summary>
    public Func<WorldGenerator?> World { get; init; } = () => null;

    /// <summary>The pins: each waypoint and what it stands for. Set before adding to the tree.</summary>
    public Func<IReadOnlyList<(Waypoint Waypoint, LogbookMarkKind Kind)>> Pins { get; init; } = () => [];

    /// <summary>The primary waypoint's id. Set before adding to the tree.</summary>
    public Func<string?> Primary { get; init; } = () => null;

    /// <summary>The local player's feet and compass heading in degrees, or null. Set before adding to the tree.</summary>
    public Func<(Vector3 Feet, float Heading)?> Player { get; init; } = () => null;

    /// <summary>True once the map round the player is drawn.</summary>
    public bool HasPicture => _texture is not null;

    /// <summary>The pins drawn last frame, where they sit from the top-left corner, and whether each waits on the rim.</summary>
    public IReadOnlyList<(Waypoint Waypoint, LogbookMarkKind Kind, Vector2 At, bool AtEdge)> Shown => _shown;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        _font = ThemeDB.FallbackFont;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || World() is not { } world || Player() is not { } me || Size.X < 1)
            return;
        if (world != _world)
        {
            // A new world (a client got the host's seed): start over.
            _world = world;
            _texture = null;
            _drawn = null;
            _job = null;
        }
        var mpp = Settings.MinimapMetresPerPixel;
        _centre = new System.Numerics.Vector2(me.Feet.X, me.Feet.Z);
        if (_job is { IsCompleted: true } job)
        {
            _job = null;
            if (job.IsCompletedSuccessfully)
            {
                _texture = ImageTexture.CreateFromImage(Image.CreateFromData(_asked.Pixels, _asked.Pixels, false, Image.Format.Rgb8, job.Result));
                _drawn = (_asked.Min, _asked.Metres);
            }
        }
        var cover = Minimap.Cover(_centre, mpp, Size.X);
        if (_job is null && (_drawn is not { } drawn || !Mathf.IsEqualApprox(drawn.Metres, cover.Metres) || Minimap.NeedsRedraw(drawn.Min, drawn.Metres, _centre)))
        {
            var pixels = Math.Max(1, (int)MathF.Round(cover.Metres / mpp));
            _asked = (cover.Min, cover.Metres, pixels);
            var interval = Settings.ContourInterval;
            _job = Task.Run(() => WorldMap.Render(world, cover.Min, mpp, pixels, pixels, interval));
        }
        _shown = [.. Pins().Select(p =>
        {
            var (at, edge) = Minimap.Pin(_centre, new System.Numerics.Vector2(p.Waypoint.Position.X, p.Waypoint.Position.Z), mpp, Size.X, RimInset);
            return (p.Waypoint, p.Kind, at.ToGodot(), edge);
        })];
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), UiTokens.DeepNavy);
        var mpp = Settings.MinimapMetresPerPixel;
        if (_texture is { } texture && _drawn is { } drawn)
        {
            var from = ((drawn.Min - _centre) / mpp).ToGodot() + size / 2;
            DrawTextureRect(texture, new Rect2(from, Vector2.One * drawn.Metres / mpp), false);
        }

        var primary = Primary();
        var pulse = LogbookMap.Pulse();
        foreach (var (waypoint, kind, at, edge) in _shown.OrderBy(p => p.Waypoint.Id == primary))
        {
            var r = LogbookMap.DrawPin(this, at, kind, waypoint.Id == primary, pulse, edge ? 0.7f : 0.85f);
            if (!edge && kind == LogbookMarkKind.Own)
                LogbookMap.DrawLabel(this, _font, waypoint.Name, at + new Vector2(r + 4, 4), 12);
        }
        if (Player() is { } me)
            LogbookMap.DrawPlayer(this, size / 2, me.Heading);

        // A frame, and north at the top.
        DrawRect(new Rect2(Vector2.One, size - Vector2.One * 2), UiTokens.Blue, false, 2);
        var north = Loc.T("N");
        var width = _font.GetStringSize(north, HorizontalAlignment.Left, -1, 13).X;
        LogbookMap.DrawLabel(this, _font, north, new Vector2((size.X - width) / 2, 15));
    }
}
