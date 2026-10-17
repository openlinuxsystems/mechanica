using System.Threading.Tasks;
using Godot;
using Mechanica.Core.Ui;
using Mechanica.Core.World;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Quests;

/// <summary>What a marker on the Logbook map stands for.</summary>
public enum LogbookMarkKind
{
    /// <summary>An active quest's goal: pulses blue (§10).</summary>
    Quest,

    /// <summary>A place a quest revealed.</summary>
    Place,

    /// <summary>A place the player marked.</summary>
    Own,
}

/// <summary>
/// The Logbook's map (UI spec §5, §10, §11, §13): the world drawn in blues from the generator, with the player, quest
/// goals, revealed places and the player's own markers on it. Drag to pan, wheel to zoom about the cursor. A click on a
/// marker makes it the primary waypoint (the compass follows); a right click marks a place, or removes one of the
/// player's own markers. The whole island is drawn once in the background; once the view settles, the area in view
/// is drawn again more sharply. The view (<see cref="View"/>) is kept while the Logbook is closed and when the window
/// changes size.
/// </summary>
public partial class LogbookMap : Control
{
    private ImageTexture? _base;
    private (Vector2 Min, Vector2 Metres, float Mpp, ImageTexture Texture)? _detail;
    private Task<byte[]>? _baseJob;
    private Task<byte[]>? _detailJob;
    private (System.Numerics.Vector2 Min, Vector2 Metres, float Mpp, int W, int H) _detailAsked;
    private WorldGenerator? _drawnWorld;
    private double _still;
    private bool _dragging;
    private Vector2 _pressedAt;
    private Vector2 _lastSize;
    private Font _font = null!;

    /// <summary>Map numbers. Set before adding to the tree.</summary>
    public LogbookSettings Settings { get; init; } = new();

    /// <summary>The world to draw, or null before it exists. Set before adding to the tree.</summary>
    public Func<WorldGenerator?> World { get; init; } = () => null;

    /// <summary>What to mark: each waypoint and what it stands for. Set before adding to the tree.</summary>
    public Func<IReadOnlyList<(Waypoint Waypoint, LogbookMarkKind Kind)>> Marks { get; init; } = () => [];

    /// <summary>The primary waypoint's id. Set before adding to the tree.</summary>
    public Func<string?> Primary { get; init; } = () => null;

    /// <summary>The local player's feet and compass heading in degrees, or null. Set before adding to the tree.</summary>
    public Func<(Vector3 Feet, float Heading)?> Player { get; init; } = () => null;

    /// <summary>Where the map looks; kept for as long as the map lives.</summary>
    public MapView? View { get; private set; }

    /// <summary>True once the whole-world picture is drawn.</summary>
    public bool HasMap => _base is not null;

    /// <summary>True once the sharper picture of the view is drawn.</summary>
    public bool HasDetail => _detail is not null;

    /// <summary>Raised when a marker is clicked: its waypoint id.</summary>
    public event Action<string>? Picked;

    /// <summary>Raised on a right click on open ground: the place in the world.</summary>
    public event Action<Vector3>? MarkRequested;

    /// <summary>Raised on a right click on one of the player's own markers: its id.</summary>
    public event Action<string>? RemoveRequested;

    public override void _Ready()
    {
        ClipContents = true;
        FocusMode = FocusModeEnum.Click;
        MouseDefaultCursorShape = CursorShape.Drag;
        _font = ThemeDB.FallbackFont;
        Resized += OnResized;
    }

    public override void _Process(double delta)
    {
        var world = World();
        if (world is null || !IsVisibleInTree())
            return;
        View ??= new MapView(world.WorldSize, Settings.MinMetresPerPixel);
        if (!View.Placed && Size.X > 0 && Size.Y > 0)
            View.Fit(Size.ToNumerics());
        if (world != _drawnWorld)
        {
            // A new world (a client got the host's seed): start over.
            _drawnWorld = world;
            _base = null;
            _detail = null;
            var resolution = Settings.MapResolution;
            var half = world.WorldSize / 2;
            _baseJob = Task.Run(() => WorldMap.Render(world, new(-half, -half), world.WorldSize / resolution, resolution, resolution, Settings.ContourInterval));
        }
        if (_baseJob is { IsCompleted: true } job)
        {
            _baseJob = null;
            if (job.IsCompletedSuccessfully)
                _base = Texture(job.Result, Settings.MapResolution, Settings.MapResolution);
        }
        _still += delta;
        UpdateDetail(world);
        QueueRedraw();
    }

    /// <summary>Centres the map on the world position <paramref name="at"/>.</summary>
    public void CentreOn(Vector3 at)
    {
        View?.CentreOn(new System.Numerics.Vector2(at.X, at.Z));
        Moved();
    }

    /// <summary>Shows the whole world.</summary>
    public void ShowAll()
    {
        View?.Fit(Size.ToNumerics());
        Moved();
    }

    /// <summary>Zooms by <paramref name="steps"/> wheel steps about the middle (positive zooms in).</summary>
    public void Zoom(float steps)
    {
        View?.ZoomAt((Size / 2).ToNumerics(), Size.ToNumerics(), steps, Settings.ZoomStep);
        Moved();
    }

    /// <summary>The point on the map where world position <paramref name="at"/> is, from the map's top-left.</summary>
    public Vector2 ScreenOf(Vector3 at) => View is null ? Vector2.Zero : (View.ToScreen(new(at.X, at.Z), Size.ToNumerics())).ToGodot();

    /// <summary>The waypoint drawn nearest <paramref name="point"/> within the pick radius, or null.</summary>
    public (Waypoint Waypoint, LogbookMarkKind Kind)? MarkAt(Vector2 point)
    {
        if (View is null)
            return null;
        var best = Marks().Select(m => (Mark: m, Gap: ScreenOf(m.Waypoint.Position.ToGodot()).DistanceTo(point)))
            .Where(m => m.Gap <= Settings.PickRadius).OrderBy(m => m.Gap).FirstOrDefault();
        return best.Mark.Waypoint is null ? null : best.Mark;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (View is null)
            return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                View.ZoomAt(wheel.Position.ToNumerics(), Size.ToNumerics(), (wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1) * Math.Max(wheel.Factor, 1f), Settings.ZoomStep);
                Moved();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left:
                if (left.Pressed)
                {
                    _pressedAt = left.Position;
                    _dragging = false;
                }
                else if (!_dragging && MarkAt(left.Position) is { } picked)
                {
                    Picked?.Invoke(picked.Waypoint.Id);
                }
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } right:
                if (MarkAt(right.Position) is { Kind: LogbookMarkKind.Own } own)
                    RemoveRequested?.Invoke(own.Waypoint.Id);
                else if (World() is { } world && View.ToWorld(right.Position.ToNumerics(), Size.ToNumerics()) is var ground
                         && MathF.Abs(ground.X) <= world.WorldSize / 2 && MathF.Abs(ground.Y) <= world.WorldSize / 2)
                    MarkRequested?.Invoke(new Vector3(ground.X, world.HeightAt(ground.X, ground.Y), ground.Y));
                AcceptEvent();
                break;
            case InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } drag:
                if (_dragging || drag.Position.DistanceTo(_pressedAt) > 4)
                {
                    _dragging = true;
                    View.Pan(drag.Relative.ToNumerics());
                    Moved();
                }
                AcceptEvent();
                break;
        }
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (MarkAt(atPosition) is not { } mark)
            return "";
        var name = mark.Kind == LogbookMarkKind.Own ? Loc.T("{0} (right-click to remove)", mark.Waypoint.Name) : mark.Waypoint.Name;
        if (Player() is { } me)
            name += "\n" + Loc.T("{0} m", $"{new Vector2(me.Feet.X - mark.Waypoint.Position.X, me.Feet.Z - mark.Waypoint.Position.Z).Length():0}");
        return name;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), UiTokens.DeepNavy);
        if (View is not { Placed: true } view || _drawnWorld is not { } world)
        {
            DrawCentred(Loc.T("Drawing the map…"), Size / 2, UiTokens.TextSecondary);
            return;
        }
        var half = world.WorldSize / 2;
        if (_base is { } texture)
            DrawTextureRect(texture, WorldRect(new(-half, -half), new Vector2(world.WorldSize, world.WorldSize)), false);
        else
            DrawCentred(Loc.T("Drawing the map…"), Size / 2, UiTokens.TextSecondary);
        // The sharper picture of the last settled view, where it still shows.
        if (_detail is { } detail)
            DrawTextureRect(detail.Texture, WorldRect(detail.Min, detail.Metres), false);
        DrawScale(view);

        var primary = Primary();
        var time = Time.GetTicksMsec() / 1000f;
        var pulse = 0.6f + 0.4f * Mathf.Sin(time * Mathf.Tau);
        foreach (var (waypoint, kind) in Marks().OrderBy(m => m.Waypoint.Id == primary))
        {
            var at = ScreenOf(waypoint.Position.ToGodot());
            var isPrimary = waypoint.Id == primary;
            var r = isPrimary ? 9f : kind == LogbookMarkKind.Place ? 5f : 7f;
            var colour = isPrimary ? UiTokens.Cyan with { A = pulse }
                : kind == LogbookMarkKind.Quest ? UiTokens.Blue.Lightened(0.3f * pulse)
                : UiTokens.Blue;
            if (isPrimary || kind == LogbookMarkKind.Quest)
                r *= 0.9f + 0.2f * pulse;
            Vector2[] diamond = [at + new Vector2(0, -r - 2), at + new Vector2(r + 2, 0), at + new Vector2(0, r + 2), at + new Vector2(-r - 2, 0)];
            DrawColoredPolygon(diamond, UiTokens.DeepNavy);
            DrawColoredPolygon([at + new Vector2(0, -r), at + new Vector2(r, 0), at + new Vector2(0, r), at + new Vector2(-r, 0)], colour);
            if (kind == LogbookMarkKind.Own || isPrimary)
                DrawLabel(waypoint.Name, at + new Vector2(r + 6, 5));
        }

        if (Player() is { } me)
        {
            // The player: a cyan arrow pointing the way they face (heading 0 is north, up the map).
            var at = ScreenOf(me.Feet);
            var turn = Mathf.DegToRad(me.Heading);
            Vector2 Rotate(Vector2 v) => v.Rotated(turn);
            Vector2[] arrow = [at + Rotate(new(0, -10)), at + Rotate(new(7, 8)), at + Rotate(new(0, 4)), at + Rotate(new(-7, 8))];
            DrawColoredPolygon([.. arrow.Select(p => at + (p - at) * 1.3f)], UiTokens.DeepNavy);
            DrawColoredPolygon(arrow, UiTokens.Cyan);
        }
    }

    // Asks for a sharper picture once the view has held still.
    private void Moved()
    {
        _still = 0;
        QueueRedraw();
    }

    private void UpdateDetail(WorldGenerator world)
    {
        if (_detailJob is { IsCompleted: true } job)
        {
            _detailJob = null;
            if (job.IsCompletedSuccessfully)
                _detail = (_detailAsked.Min.ToGodot(), _detailAsked.Metres, _detailAsked.Mpp, Texture(job.Result, _detailAsked.W, _detailAsked.H));
        }
        if (_detailJob is not null || View is not { Placed: true } view || _still < Settings.DetailDelaySeconds || Size.X < 1 || Size.Y < 1)
            return;
        // Only worth it when the view is sharper than the whole-world picture.
        if (view.MetresPerPixel * 1.5f > world.WorldSize / Settings.MapResolution)
            return;
        var scale = Math.Min(1f, Settings.DetailResolution / Math.Max(Size.X, Size.Y));
        var (w, h) = (Math.Max(1, (int)(Size.X * scale)), Math.Max(1, (int)(Size.Y * scale)));
        var min = view.Visible(Size.ToNumerics()).Min;
        var mpp = view.MetresPerPixel / scale;
        if (_detail is { } shown && shown.Min == min.ToGodot() && Mathf.IsEqualApprox(shown.Mpp, view.MetresPerPixel))
            return;
        _detailAsked = (min, new Vector2(w, h) * mpp, view.MetresPerPixel, w, h);
        var interval = Settings.ContourInterval;
        _detailJob = Task.Run(() => WorldMap.Render(world, min, mpp, w, h, interval));
    }

    private void OnResized()
    {
        if (Size.X <= 0 || Size.Y <= 0)
            return;
        if (View is not null && _lastSize != Size)
            View.Resized(Size.ToNumerics());
        _lastSize = Size;
        Moved();
    }

    // The screen rectangle of the world from min x/z, metres across and down.
    private Rect2 WorldRect(Vector2 min, Vector2 metres)
    {
        var from = ScreenOf(new Vector3(min.X, 0, min.Y));
        var to = ScreenOf(new Vector3(min.X + metres.X, 0, min.Y + metres.Y));
        return new Rect2(from, to - from);
    }

    // A bar of a round number of metres in the bottom-left corner.
    private void DrawScale(MapView view)
    {
        var target = 120f * view.MetresPerPixel;
        var metres = new[] { 1f, 2f, 5f }.SelectMany(m => Enumerable.Range(0, 6).Select(p => m * MathF.Pow(10, p))).Where(m => m <= target).DefaultIfEmpty(1f).Max();
        var length = metres / view.MetresPerPixel;
        var from = new Vector2(12, Size.Y - 14);
        DrawLine(from, from + new Vector2(length, 0), UiTokens.Text, 2);
        DrawLine(from - new Vector2(0, 5), from + new Vector2(0, 1), UiTokens.Text, 2);
        DrawLine(from + new Vector2(length, -5), from + new Vector2(length, 1), UiTokens.Text, 2);
        DrawLabel(metres >= 1000 ? Loc.T("{0} km", metres / 1000) : Loc.T("{0} m", metres), from + new Vector2(0, -8));
    }

    private void DrawLabel(string text, Vector2 at)
    {
        DrawStringOutline(_font, at, text, HorizontalAlignment.Left, -1, 13, 4, UiTokens.DeepNavy);
        DrawString(_font, at, text, HorizontalAlignment.Left, -1, 13, UiTokens.Text);
    }

    private void DrawCentred(string text, Vector2 at, Color colour)
    {
        var size = _font.GetStringSize(text, HorizontalAlignment.Left, -1, 15);
        DrawString(_font, at - size / 2, text, HorizontalAlignment.Left, -1, 15, colour);
    }

    private static ImageTexture Texture(byte[] rgb, int w, int h) => ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgb8, rgb));
}
