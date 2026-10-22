using Godot;
using Mechanica.Core.Survival;
using Mechanica.Core.Ui;
using Mechanica.Game.Net;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Ui;

/// <summary>
/// The compass bar (UI spec §13): top centre, at the top of the safe area and 40% of the screen wide, with the
/// player's heading in the middle, the points and a tick every 15°. Waypoints (quest goals and places
/// found) are blue diamonds with their distance under them within 500 m; the primary one is larger and
/// pulses cyan, and clicking a diamond (while the mouse is free, as with the logbook open) makes it primary.
/// The player's own knockout bags are a bag (<see cref="BagIcon"/>) instead of a diamond, taller so the sack reads.
/// A waypoint behind the player sits at the edge it is nearer turning to, with an arrow. Within 100 m its
/// diamond (or bag) also floats over the place itself, hidden behind whatever is in the way. Every other player in the
/// world, AI agents included, is a white dot with their name and distance under it at any range, pinned to an
/// edge with an arrow like a waypoint when behind. The bar hides while a
/// full-screen window is open and with Show compass off (Options, Interface), and is 20% fainter in first person.
/// </summary>
public partial class CompassHud : CanvasLayer
{
    /// <summary>Metres within which a waypoint's diamond also shows in the world.</summary>
    public const float WorldMarkerRange = 100f;

    private const float TickEvery = 15f;

    // Bag heights on the bar, normal and primary: a little taller than the 10 and 14 px diamonds, so the sack's shape
    // reads at a glance, and still inside the 24 px strip.
    private const float BagHeight = 15f;
    private const float PrimaryBagHeight = 19f;

    private readonly Dictionary<string, GeometryInstance3D> _worldMarkers = [];
    private Control _bar = null!;
    private Node3D _worldRoot = null!;
    private Font _font = null!;
    private List<(Waypoint Waypoint, CompassMark Mark)> _marks = [];
    private List<(int Peer, string Name, CompassMark Mark)> _playerMarks = [];
    private string? _primary;
    private float _heading;

    /// <summary>Players: the local one the bar turns with, and everyone else it marks. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The waypoints now, the default primary first. Set before adding to the tree.</summary>
    public Func<IReadOnlyList<Waypoint>> Waypoints { get; set; } = () => [];

    /// <summary>True while a full-screen window (inventory, pause menu) is open. Set before adding to the tree.</summary>
    public Func<bool> Covered { get; init; } = () => false;

    /// <summary>The bar.</summary>
    public Control Bar => _bar;

    /// <summary>The bar's lower edge (distances included) in pixels while it shows, else 0, so what else sits top centre goes under it.</summary>
    public static float Bottom { get; private set; }

    /// <summary>The heading the bar is centred on, 0 to 360°.</summary>
    public float Heading => _heading;

    /// <summary>The waypoints on the bar as last drawn.</summary>
    public IReadOnlyList<(Waypoint Waypoint, CompassMark Mark)> Marks => _marks;

    /// <summary>The other players on the bar as last drawn, by peer.</summary>
    public IReadOnlyList<(int Peer, string Name, CompassMark Mark)> PlayerMarks => _playerMarks;

    /// <summary>The primary waypoint's id: the player's pick while it exists, else the first waypoint's.</summary>
    public string? Primary => _marks.Any(m => m.Waypoint.Id == _primary) ? _primary : _marks.FirstOrDefault().Waypoint?.Id;

    /// <summary>The world-space diamond (a bag for a knockout bag) over a waypoint, or null when none shows.</summary>
    public GeometryInstance3D? WorldMarkerOf(string id) => _worldMarkers.GetValueOrDefault(id) is { } m && IsInstanceValid(m) && m.Visible ? m : null;

    public override void _Ready()
    {
        Layer = 2;
        AddToGroup(HudRoot.Group);
        _font = ThemeDB.FallbackFont;
        _bar = UiInput.KeepMouse(new Control { Name = "Compass" });
        _bar.Draw += DrawBar;
        _bar.GuiInput += OnBarInput;
        AddChild(_bar);
        _worldRoot = new Node3D { Name = "WorldMarkers" };
        AddChild(_worldRoot);
        GetViewport().SizeChanged += Layout;
        Layout();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        Bottom = 0;
    }

    public override void _Process(double delta)
    {
        var player = Players.LocalPlayer;
        var show = GameSettings.Current.Interface.ShowCompass && player is not null && !Covered();
        _bar.Visible = show;
        Bottom = show && !HudRoot.Hidden ? _bar.Position.Y + _bar.Size.Y : 0;
        _marks.Clear();
        _playerMarks.Clear();
        if (player is null)
        {
            HideWorldMarkers();
            return;
        }
        var from = player.GlobalPosition.ToNumerics();
        _heading = Compass.Heading(player.Camera.GlobalRotation.Y);
        _marks = [.. Waypoints().Select(w => (w, Compass.Mark(from, _heading, w.Position)))];
        _playerMarks = [.. Players.Players.Where(p => p.Value != player)
            .Select(p => (p.Key, p.Value.DisplayName, Compass.Mark(from, _heading, p.Value.GlobalPosition.ToNumerics())))];
        UpdateWorldMarkers(show && !HudRoot.Hidden);
        _bar.Modulate = Colors.White with { A = player.ThirdPerson ? 1f : 0.8f };
        _bar.QueueRedraw();
    }

    /// <summary>Makes <paramref name="id"/> the primary waypoint (as clicking it does).</summary>
    public void SetPrimary(string id) => _primary = id;

    // Top centre, against the top of the safe area (Chase asked for the gap above it gone) and 40% of the screen wide.
    private void Layout()
    {
        var size = GetViewport().GetVisibleRect().Size;
        _bar.Size = new Vector2(size.X * 0.4f, 44);
        _bar.Position = new Vector2((size.X - _bar.Size.X) / 2, HudRoot.Margin(GetViewport()).Y);
    }

    private float XOf(float mark) => _bar.Size.X / 2 * (1 + mark * 0.94f);

    private void DrawBar()
    {
        var w = _bar.Size.X;
        _bar.DrawRect(new Rect2(0, 0, w, 24), UiTokens.Panel);
        // Ticks every 15°, longer with a name at the points.
        for (var bearing = 0f; bearing < 360f; bearing += TickEvery)
        {
            if (Compass.X(_heading, bearing) is not { } x)
                continue;
            var at = XOf(x);
            var point = bearing % 45f == 0f;
            if (point)
            {
                var cardinal = Compass.Points[(int)(bearing / 45f)];
                var name = Loc.T(cardinal);
                var size = _font.GetStringSize(name, HorizontalAlignment.Left, -1, 14);
                _bar.DrawString(_font, new Vector2(at - size.X / 2, 17), name, HorizontalAlignment.Left, -1, 14, cardinal.Length == 1 ? UiTokens.Text : UiTokens.TextSecondary);
            }
            else
            {
                _bar.DrawLine(new Vector2(at, 16), new Vector2(at, 23), UiTokens.TextSecondary, 1);
            }
        }
        _bar.DrawLine(new Vector2(w / 2, 0), new Vector2(w / 2, 6), UiTokens.Cyan, 2);
        DrawPlayers(w);
        var primary = Primary;
        var pulse = 0.65f + 0.35f * Mathf.Sin((float)Time.GetTicksMsec() / 1000f * Mathf.Tau);
        foreach (var (waypoint, mark) in _marks.OrderBy(m => m.Waypoint.Id == primary))
        {
            var isPrimary = waypoint.Id == primary;
            var x = XOf(mark.X);
            var colour = isPrimary ? UiTokens.Cyan with { A = pulse } : UiTokens.Blue;
            float r;
            if (KnockoutBags.IsBag(waypoint))
            {
                var height = isPrimary ? PrimaryBagHeight : BagHeight;
                r = height * 0.46f;
                BagIcon.Draw(_bar, new Vector2(x, 12), height, colour);
            }
            else
            {
                r = isPrimary ? 7f : 5f;
                _bar.DrawColoredPolygon([new(x, 12 - r), new(x + r, 12), new(x, 12 + r), new(x - r, 12)], colour);
            }
            if (mark.Behind)
            {
                // An arrow pointing the way to turn.
                var s = mark.X < 0 ? -1f : 1f;
                var tip = x + s * (r + 8);
                _bar.DrawColoredPolygon([new(tip, 12), new(tip - s * 6, 7), new(tip - s * 6, 17)], colour);
            }
            if (Compass.DistanceLabel(mark.Distance) is not null && Loc.T("{0} m", $"{mark.Distance:0}") is var label)
            {
                var size = _font.GetStringSize(label, HorizontalAlignment.Left, -1, 12);
                _bar.DrawString(_font, new Vector2(Mathf.Clamp(x - size.X / 2, 0, w - size.X), 38), label, HorizontalAlignment.Left, -1, 12, UiTokens.Text);
            }
        }
    }

    // Other players under the waypoints: a white dot with a navy rim, the name and distance under it.
    private void DrawPlayers(float w)
    {
        foreach (var (_, name, mark) in _playerMarks.OrderByDescending(m => m.Mark.Distance))
        {
            var x = XOf(mark.X);
            _bar.DrawCircle(new Vector2(x, 12), 5.5f, UiTokens.DeepNavy);
            _bar.DrawCircle(new Vector2(x, 12), 4f, UiTokens.Text);
            if (mark.Behind)
            {
                var s = mark.X < 0 ? -1f : 1f;
                var tip = x + s * 13;
                _bar.DrawColoredPolygon([new(tip, 12), new(tip - s * 6, 7), new(tip - s * 6, 17)], UiTokens.Text);
            }
            var distance = mark.Distance >= 1000 ? Loc.T("{0} km", $"{mark.Distance / 1000:0.0}") : Loc.T("{0} m", $"{mark.Distance:0}");
            // The name is the player's own text and the distance is already translated; only the dot joins them.
            var label = $"{name}  ·  {distance}";
            var size = _font.GetStringSize(label, HorizontalAlignment.Left, -1, 12);
            _bar.DrawString(_font, new Vector2(Mathf.Clamp(x - size.X / 2, 0, w - size.X), 38), label, HorizontalAlignment.Left, -1, 12, UiTokens.TextSecondary);
        }
    }

    private void OnBarInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            return;
        var hit = _marks.Select(m => (m.Waypoint, Gap: Mathf.Abs(XOf(m.Mark.X) - click.Position.X))).Where(m => m.Gap <= 10).OrderBy(m => m.Gap).FirstOrDefault();
        if (hit.Waypoint is { } waypoint)
            SetPrimary(waypoint.Id);
    }

    private void UpdateWorldMarkers(bool show)
    {
        var camera = GetViewport().GetCamera3D();
        var primary = Primary;
        var seen = new HashSet<string>();
        foreach (var (waypoint, mark) in _marks)
        {
            seen.Add(waypoint.Id);
            if (!_worldMarkers.TryGetValue(waypoint.Id, out var marker) || !IsInstanceValid(marker))
            {
                marker = KnockoutBags.IsBag(waypoint)
                    ? new Sprite3D
                    {
                        Name = "Bag",
                        Texture = BagIcon.Texture(),
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                        FixedSize = true,
                        PixelSize = 0.0011f,
                        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
                    }
                    : new Label3D
                    {
                        Name = "Waypoint",
                        Text = "◆",
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                        FixedSize = true,
                        PixelSize = 0.002f,
                        FontSize = 40,
                        OutlineSize = 6,
                        OutlineModulate = UiTokens.DeepNavy,
                    };
                AddWorldMarker(marker);
                _worldMarkers[waypoint.Id] = marker;
            }
            marker.GlobalPosition = waypoint.Position.ToGodot() + Vector3.Up * 2f;
            var colour = waypoint.Id == primary ? UiTokens.Cyan : UiTokens.Blue;
            if (marker is Label3D label)
                label.Modulate = colour;
            else if (marker is Sprite3D sprite)
                sprite.Modulate = colour;
            marker.Visible = show && mark.Distance <= WorldMarkerRange && camera is not null;
        }
        foreach (var id in _worldMarkers.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            if (IsInstanceValid(_worldMarkers[id]))
                _worldMarkers[id].QueueFree();
            _worldMarkers.Remove(id);
        }
    }

    // Drawn in the world with depth testing, so the ground and buildings in the way hide it (line of sight).
    private void AddWorldMarker(GeometryInstance3D marker) => _worldRoot.AddChild(marker);

    private void HideWorldMarkers()
    {
        foreach (var marker in _worldMarkers.Values.Where(IsInstanceValid))
            marker.Visible = false;
    }
}
