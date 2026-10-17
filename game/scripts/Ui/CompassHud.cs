using Godot;
using Mechanica.Core.Ui;
using Mechanica.Game.Net;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Ui;

/// <summary>
/// The compass bar (UI spec §13): top centre, 10% from the top and 40% of the screen wide, with the
/// player's heading in the middle, the points and a tick every 15°. Waypoints (quest goals and places
/// found) are blue diamonds with their distance under them within 500 m; the primary one is larger and
/// pulses cyan, and clicking a diamond (while the mouse is free, as with the logbook open) makes it primary.
/// A waypoint behind the player sits at the edge it is nearer turning to, with an arrow. Within 100 m a
/// diamond also floats over the place itself, hidden behind whatever is in the way. The bar hides while a
/// full-screen window is open and with Show compass off (Options, Interface), and is 20% fainter in first person.
/// </summary>
public partial class CompassHud : CanvasLayer
{
    /// <summary>Metres within which a waypoint's diamond also shows in the world.</summary>
    public const float WorldMarkerRange = 100f;

    private const float TickEvery = 15f;

    private readonly Dictionary<string, Label3D> _worldMarkers = [];
    private Control _bar = null!;
    private Node3D _worldRoot = null!;
    private Font _font = null!;
    private List<(Waypoint Waypoint, CompassMark Mark)> _marks = [];
    private string? _primary;
    private float _heading;

    /// <summary>Players, for the local one. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The waypoints now, the default primary first. Set before adding to the tree.</summary>
    public Func<IReadOnlyList<Waypoint>> Waypoints { get; set; } = () => [];

    /// <summary>True while a full-screen window (inventory, pause menu) is open. Set before adding to the tree.</summary>
    public Func<bool> Covered { get; init; } = () => false;

    /// <summary>The bar.</summary>
    public Control Bar => _bar;

    /// <summary>The heading the bar is centred on, 0 to 360°.</summary>
    public float Heading => _heading;

    /// <summary>The waypoints on the bar as last drawn.</summary>
    public IReadOnlyList<(Waypoint Waypoint, CompassMark Mark)> Marks => _marks;

    /// <summary>The primary waypoint's id: the player's pick while it exists, else the first waypoint's.</summary>
    public string? Primary => _marks.Any(m => m.Waypoint.Id == _primary) ? _primary : _marks.FirstOrDefault().Waypoint?.Id;

    /// <summary>The world-space diamond over a waypoint, or null when none shows.</summary>
    public Label3D? WorldMarkerOf(string id) => _worldMarkers.GetValueOrDefault(id) is { } m && IsInstanceValid(m) && m.Visible ? m : null;

    public override void _Ready()
    {
        Layer = 2;
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

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    public override void _Process(double delta)
    {
        var player = Players.LocalPlayer;
        var show = GameSettings.Current.Interface.ShowCompass && player is not null && !Covered();
        _bar.Visible = show;
        _marks.Clear();
        if (player is null)
        {
            HideWorldMarkers();
            return;
        }
        var from = player.GlobalPosition.ToNumerics();
        _heading = Compass.Heading(player.Camera.GlobalRotation.Y);
        _marks = [.. Waypoints().Select(w => (w, Compass.Mark(from, _heading, w.Position)))];
        UpdateWorldMarkers(show);
        _bar.Modulate = Colors.White with { A = player.ThirdPerson ? 1f : 0.8f };
        _bar.QueueRedraw();
    }

    /// <summary>Makes <paramref name="id"/> the primary waypoint (as clicking it does).</summary>
    public void SetPrimary(string id) => _primary = id;

    // Top centre, 10% from the top edge and 40% of the screen wide.
    private void Layout()
    {
        var size = GetViewport().GetVisibleRect().Size;
        _bar.Size = new Vector2(size.X * 0.4f, 44);
        _bar.Position = new Vector2((size.X - _bar.Size.X) / 2, size.Y * 0.1f);
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
                var name = Compass.Points[(int)(bearing / 45f)];
                var size = _font.GetStringSize(name, HorizontalAlignment.Left, -1, 14);
                _bar.DrawString(_font, new Vector2(at - size.X / 2, 17), name, HorizontalAlignment.Left, -1, 14, name.Length == 1 ? UiTokens.Text : UiTokens.TextSecondary);
            }
            else
            {
                _bar.DrawLine(new Vector2(at, 16), new Vector2(at, 23), UiTokens.TextSecondary, 1);
            }
        }
        _bar.DrawLine(new Vector2(w / 2, 0), new Vector2(w / 2, 6), UiTokens.Cyan, 2);
        var primary = Primary;
        var pulse = 0.65f + 0.35f * Mathf.Sin((float)Time.GetTicksMsec() / 1000f * Mathf.Tau);
        foreach (var (waypoint, mark) in _marks.OrderBy(m => m.Waypoint.Id == primary))
        {
            var isPrimary = waypoint.Id == primary;
            var x = XOf(mark.X);
            var r = isPrimary ? 7f : 5f;
            var colour = isPrimary ? UiTokens.Cyan with { A = pulse } : UiTokens.Blue;
            _bar.DrawColoredPolygon([new(x, 12 - r), new(x + r, 12), new(x, 12 + r), new(x - r, 12)], colour);
            if (mark.Behind)
            {
                // An arrow pointing the way to turn.
                var s = mark.X < 0 ? -1f : 1f;
                var tip = x + s * (r + 8);
                _bar.DrawColoredPolygon([new(tip, 12), new(tip - s * 6, 7), new(tip - s * 6, 17)], colour);
            }
            if (Compass.DistanceLabel(mark.Distance) is { } label)
            {
                var size = _font.GetStringSize(label, HorizontalAlignment.Left, -1, 12);
                _bar.DrawString(_font, new Vector2(Mathf.Clamp(x - size.X / 2, 0, w - size.X), 38), label, HorizontalAlignment.Left, -1, 12, UiTokens.Text);
            }
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
                marker = new Label3D
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
            marker.Modulate = waypoint.Id == primary ? UiTokens.Cyan : UiTokens.Blue;
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
    private void AddWorldMarker(Label3D marker) => _worldRoot.AddChild(marker);

    private void HideWorldMarkers()
    {
        foreach (var marker in _worldMarkers.Values.Where(IsInstanceValid))
            marker.Visible = false;
    }
}
