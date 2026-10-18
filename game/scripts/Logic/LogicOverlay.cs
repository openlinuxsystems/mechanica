using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Logic;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Logic;

/// <summary>
/// Connection visuals (chunk 10, UI spec §12), drawn on screen over the world so they keep their thickness at any
/// distance or resolution. Every logic link of the Creations near the local player is a 3 px blue line with a cyan
/// core and a chevron every 40 px pointing from source to target; lines between the same two parts sit side by side.
/// With the Connect Tool out every part with logic shows a node coloured by its kind (blue logic, cyan power, white
/// with a blue ring for bearings, gray structural) that pulses cyan while its signal is on, grows under the
/// crosshair and has a curved arrow when something drives it and it can be reversed (a bearing linked to a seat, a
/// controller or an engine). A part a seat or a controller is linked to shows its order number beside its node. While the tool drags a line, the preview follows the
/// aim with a little slack, and nodes it cannot end on fade (§9). Links the host has not confirmed show at once and
/// fade out red if it refuses them. Lines show while the tool is out or the visualizer is on (F7); otherwise a
/// link flashes briefly when its source's signal changes. Signals are read with <c>InspectCreation</c> a few times
/// a second, so a client sees what the host's bus does.
/// </summary>
public partial class LogicOverlay : CanvasLayer
{
    /// <summary>Seconds between signal reads.</summary>
    public const double PollSeconds = 0.2;

    private readonly Dictionary<int, IReadOnlyDictionary<int, float>> _signals = [];
    private readonly HashSet<int> _asking = [];
    private readonly Dictionary<(int Creation, LogicLink Link), double> _flash = [];
    private readonly List<Line> _lines = [];
    private readonly List<Dot> _dots = [];
    private Painter _canvas = null!;
    private (Vector2 From, Vector2 Control, Vector2 To)? _preview;
    private double _sincePoll;
    private double _time;

    /// <summary>A line to draw: screen ends, its colour and whether it is hovered.</summary>
    private readonly record struct Line(Vector2 From, Vector2 To, Color Outer, Color Core, bool Chevrons, bool Bright);

    /// <summary>A node to draw.</summary>
    private readonly record struct Dot(Vector2 At, float Radius, PortKind Kind, float Alpha, bool Pulse, bool Ring, bool? Reversed, string? Numbers);

    /// <summary>One link on screen: where its ends are, lanes applied.</summary>
    public readonly record struct Segment(int Creation, LogicLink Link, Vector2 From, Vector2 To);

    /// <summary>The Creations to draw. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Players, for the local player and camera. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Where signal reads go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Building settings (the look of connections). Set before adding to the tree.</summary>
    public BuildSync Build { get; init; } = null!;

    /// <summary>False in automated tests: F7 does nothing.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>True when the player turned the visualizer on (F7).</summary>
    public bool Enabled { get; set; }

    /// <summary>True while something else wants links shown.</summary>
    public bool ShowLinks { get; set; }

    /// <summary>The Connect Tool, whose nodes, hover and drag are drawn while it is out; optional.</summary>
    public Func<ConnectTool?>? Connector { get; init; }

    /// <summary>Links drawn on the last frame, with whether each source was on, for tests.</summary>
    public IReadOnlyList<(int Creation, LogicLink Link, bool On)> Drawn { get; private set; } = [];

    /// <summary>Nodes drawn on the last frame (Creation, part, opacity), for tests.</summary>
    public IReadOnlyList<(int Creation, int Part, float Alpha)> DrawnNodes { get; private set; } = [];

    /// <summary>Order numbers drawn on the last frame (Creation, part, text), for tests.</summary>
    public IReadOnlyList<(int Creation, int Part, string Text)> DrawnNumbers { get; private set; } = [];

    /// <summary>Links flashing now because their signal changed with the lines hidden, for tests.</summary>
    public IReadOnlyCollection<(int Creation, LogicLink Link)> Flashing => _flash.Keys;

    /// <summary>True while every line shows (the Connect Tool out, or F7).</summary>
    public bool ShowingAll => Enabled || ShowLinks || Tool is not null;

    private ConnectTool? Tool => Connector?.Invoke() is { Active: true } tool ? tool : null;

    private ConnectSettings Look => Build.Settings?.Connect ?? new ConnectSettings();

    public override void _Ready()
    {
        Name = "LogicOverlay";
        Layer = 1;
        _canvas = new Painter { Name = "Canvas", MouseFilter = Control.MouseFilterEnum.Ignore, Paints = Paint };
        AddChild(_canvas);
        _canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ReadDeviceInput || !GameInput.Pressed(@event, InputActions.LogicVisualizer))
            return;
        Enabled = !Enabled;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        foreach (var key in _flash.Keys.ToList())
        {
            if ((_flash[key] -= delta) <= 0)
                _flash.Remove(key);
        }
        var centre = Players.LocalPlayer?.GlobalPosition;
        var nearby = ConnectionNodes.Nearby(Creations, centre, Look.Range).Where(n => n.Creation.Links.Count > 0).ToList();
        _sincePoll += delta;
        if (_sincePoll >= PollSeconds)
        {
            _sincePoll = 0;
            foreach (var node in nearby)
                Poll(node.Creation.Id);
        }
        Layout();
        _canvas.QueueRedraw();
    }

    /// <summary>Reads a Creation's signals now (the answer arrives through the Action API).</summary>
    public void Poll(int creationId)
    {
        if (!_asking.Add(creationId))
            return;
        Actions.Request(LogicActions.InspectCreation, new InspectCreationArgs(creationId), result =>
        {
            _asking.Remove(creationId);
            if (result is not { Ok: true, Value: { } value })
            {
                _signals.Remove(creationId);
                return;
            }
            var inspection = value.Deserialize<CreationInspection>(ActionJson.Options)!;
            var signals = inspection.Parts.Where(p => p.Output != 0).ToDictionary(p => p.Id, p => p.Output);
            // A source whose signal changed flashes its links (§12), from the second read on.
            if (_signals.TryGetValue(creationId, out var before) && Creations.Find(creationId) is { } creation)
            {
                foreach (var link in creation.Links)
                {
                    if (before.GetValueOrDefault(link.From) != signals.GetValueOrDefault(link.From))
                        _flash[(creationId, link)] = Look.FlashSeconds;
                }
            }
            _signals[creationId] = signals;
        });
    }

    /// <summary>The output this peer last read for a part (0 when off or not read yet).</summary>
    public float SignalOf(int creationId, int partId) =>
        _signals.GetValueOrDefault(creationId)?.GetValueOrDefault(partId) ?? 0f;

    /// <summary>Screen pixels per 1080 p pixel, so sizes in <see cref="ConnectSettings"/> keep their look at any resolution.</summary>
    public static float PixelScale(Viewport viewport) => Math.Max(0.5f, viewport.GetVisibleRect().Size.Y / 1080f);

    /// <summary>
    /// Every link of the Creations within range of <paramref name="centre"/> as it shows on screen through
    /// <paramref name="camera"/>, with lines between the same two parts moved apart. Links with an end behind the
    /// camera are left out.
    /// </summary>
    public static IEnumerable<Segment> Segments(Camera3D camera, CreationWorld creations, Vector3 centre, ConnectSettings look, float scale)
    {
        foreach (var node in ConnectionNodes.Nearby(creations, centre, look.Range))
        {
            var creation = node.Creation;
            var links = creation.Links.ToList();
            var lanes = ConnectionLayout.Lanes(links);
            for (var i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (ConnectionNodes.Centre(node, link.From) is not { } from || ConnectionNodes.Centre(node, link.To) is not { } to
                    || camera.IsPositionBehind(from) || camera.IsPositionBehind(to))
                    continue;
                var a = camera.UnprojectPosition(from);
                var b = camera.UnprojectPosition(to);
                var offset = ConnectionLayout.LaneOffset(a.ToNumerics(), b.ToNumerics(), lanes[i].Index, lanes[i].Count, look.LaneGap * scale, link.From < link.To).ToGodot();
                yield return new Segment(creation.Id, link, a + offset, b + offset);
            }
        }
    }

    // Works out what to draw this frame (and what Drawn reports), so tests see it headless too.
    private void Layout()
    {
        _lines.Clear();
        _dots.Clear();
        _preview = null;
        var drawn = new List<(int, LogicLink, bool)>();
        var drawnNodes = new List<(int, int, float)>();
        var drawnNumbers = new List<(int, int, string)>();
        var camera = Players.LocalPlayer?.Camera;
        var viewport = GetViewport();
        if (camera is null || viewport is null)
        {
            Drawn = [];
            DrawnNodes = [];
            DrawnNumbers = [];
            return;
        }
        var look = Look;
        var scale = PixelScale(viewport);
        var tool = Tool;
        var all = ShowingAll;
        var rollback = Build.Settings?.Placement.RollbackSeconds ?? 0.4;
        var pending = tool?.Pending ?? Connector?.Invoke()?.Pending ?? [];
        var removing = pending.Where(p => !p.Adding && p.Refused is null).Select(p => (p.Creation, p.Link)).ToHashSet();
        float Flash((int, LogicLink) key) => _flash.TryGetValue(key, out var left) ? (float)(left / Math.Max(look.FlashSeconds, 1e-3)) : 0f;
        bool Shown((int, LogicLink) key) => (all || Flash(key) > 0) && !removing.Contains(key);
        // What shows, wherever the camera looks.
        foreach (var node in ConnectionNodes.Nearby(Creations, Players.LocalPlayer?.GlobalPosition, look.Range))
        {
            foreach (var link in node.Creation.Links.Where(l => Shown((node.Creation.Id, l))))
                drawn.Add((node.Creation.Id, link, SignalOf(node.Creation.Id, link.From) != 0));
        }
        foreach (var segment in Segments(camera, Creations, camera.GlobalPosition, look, scale))
        {
            var key = (segment.Creation, segment.Link);
            if (!Shown(key))
                continue;
            var on = SignalOf(segment.Creation, segment.Link.From) != 0;
            var alpha = all ? 1f : Flash(key);
            var hovered = tool?.HoveredLink == key;
            var outer = (hovered ? UiTokens.Blue.Lightened(0.35f) : UiTokens.Blue) with { A = alpha };
            var core = (hovered ? UiTokens.Text : UiTokens.Cyan) with { A = alpha * (on || hovered ? 1f : 0.55f) };
            _lines.Add(new Line(segment.From, segment.To, outer, core, Chevrons: true, Bright: hovered));
        }
        // Links sent and not yet confirmed show at once; refused ones fade out red (§9 rollback).
        foreach (var link in pending.Where(p => p.Adding || p.Refused is not null))
        {
            if (Creations.NodeOf(link.Creation) is not { } node || ConnectionNodes.Centre(node, link.Link.From) is not { } from
                || ConnectionNodes.Centre(node, link.Link.To) is not { } to || camera.IsPositionBehind(from) || camera.IsPositionBehind(to)
                || (link.Refused is null && node.Creation.Links.Contains(link.Link)))
                continue;
            var fade = link.Refused is { } refused ? 1f - (float)Math.Clamp(refused / Math.Max(rollback, 1e-3), 0, 1) : 0.8f;
            var colour = link.Refused is null ? UiTokens.Blue : UiTokens.Error;
            _lines.Add(new Line(camera.UnprojectPosition(from), camera.UnprojectPosition(to), colour with { A = fade },
                (link.Refused is null ? UiTokens.Cyan : UiTokens.Error.Lightened(0.3f)) with { A = fade }, Chevrons: true, Bright: false));
        }
        Drawn = drawn;
        if (tool is null)
        {
            DrawnNodes = [];
            DrawnNumbers = [];
            return;
        }
        var radius = look.NodePixels / 2 * scale;
        foreach (var node in tool.Nodes)
        {
            if (camera.IsPositionBehind(node.Position))
                continue;
            var source = tool.DragFrom is { } from && from.Creation == node.Creation && from.Part == node.Part;
            var alpha = tool.Dragging && !source && !tool.CanEnd(node) ? look.InvalidAlpha : 1f;
            var hovered = tool.HoveredNode is { } h && h.Creation == node.Creation && h.Part == node.Part;
            var swept = tool.Swept.Any(n => n.Creation == node.Creation && n.Part == node.Part);
            var active = SignalOf(node.Creation, node.Part) != 0;
            // A part linked to several seats or controllers shows each one's number, in part order of the sources.
            var numbers = node.Numbers is { Count: > 0 } list ? string.Join(" ", list.Select(n => n.Number)) : null;
            _dots.Add(new Dot(camera.UnprojectPosition(node.Position), radius * (hovered || source ? look.HoverScale : 1f), node.Kind, alpha,
                active, swept || source, node.Reversible ? tool.ShownReversed(node) : null, numbers));
            drawnNodes.Add((node.Creation, node.Part, alpha));
            if (numbers is not null)
                drawnNumbers.Add((node.Creation, node.Part, numbers));
        }
        DrawnNodes = drawnNodes;
        DrawnNumbers = drawnNumbers;
        if (tool.DragFrom is { } start && !camera.IsPositionBehind(start.Position))
        {
            var end = tool.DragEnd;
            if (!camera.IsPositionBehind(end))
            {
                var a = camera.UnprojectPosition(start.Position);
                var b = camera.UnprojectPosition(end);
                _preview = (a, ConnectionLayout.Tension(a.ToNumerics(), b.ToNumerics(), tool.HoveredNode is not null).ToGodot(), b);
            }
        }
    }

    private void Paint(Control canvas)
    {
        var scale = PixelScale(canvas.GetViewport());
        var look = Look;
        var width = look.LineWidth * scale;
        foreach (var line in _lines)
        {
            canvas.DrawLine(line.From, line.To, UiTokens.DeepNavy with { A = 0.45f * line.Outer.A }, width + 2 * scale, antialiased: true);
            canvas.DrawLine(line.From, line.To, line.Outer, width + (line.Bright ? scale : 0), antialiased: true);
            canvas.DrawLine(line.From, line.To, line.Core, Math.Max(1f, width / 3), antialiased: true);
            if (line.Chevrons)
            {
                foreach (var at in ConnectionLayout.Chevrons(line.From.ToNumerics(), line.To.ToNumerics(), look.ChevronSpacing * scale))
                    Chevron(canvas, at.ToGodot(), (line.To - line.From).Normalized(), 4 * scale, line.Core);
            }
        }
        if (_preview is var (from, control, to))
        {
            var points = Enumerable.Range(0, 17).Select(i => ConnectionLayout.Curve(from.ToNumerics(), control.ToNumerics(), to.ToNumerics(), i / 16f).ToGodot()).ToArray();
            canvas.DrawPolyline(points, UiTokens.Blue, width, antialiased: true);
            canvas.DrawPolyline(points, UiTokens.Cyan, Math.Max(1f, width / 3), antialiased: true);
            Chevron(canvas, to, (to - points[^2]).Normalized(), 5 * scale, UiTokens.Cyan);
        }
        var phase = look.PulseSeconds > 0 ? (float)(_time % look.PulseSeconds / look.PulseSeconds) : 0f;
        foreach (var dot in _dots)
        {
            var fill = ConnectionNodes.Colour(dot.Kind) with { A = dot.Alpha };
            if (dot.Pulse)
                canvas.DrawCircle(dot.At, dot.Radius * (1 + 0.7f * phase), UiTokens.Cyan with { A = 0.45f * (1 - phase) * dot.Alpha });
            canvas.DrawCircle(dot.At, dot.Radius + scale, UiTokens.DeepNavy with { A = 0.7f * dot.Alpha });
            canvas.DrawCircle(dot.At, dot.Radius, fill);
            if (dot.Kind == PortKind.Bearing)
                canvas.DrawArc(dot.At, dot.Radius - scale, 0, Mathf.Tau, 24, UiTokens.Blue with { A = dot.Alpha }, 2 * scale, antialiased: true);
            if (dot.Ring)
                canvas.DrawArc(dot.At, dot.Radius + 3 * scale, 0, Mathf.Tau, 24, UiTokens.Cyan with { A = dot.Alpha }, 2 * scale, antialiased: true);
            if (dot.Reversed is { } reversed)
                CurvedArrow(canvas, dot.At + new Vector2(dot.Radius + 6 * scale, -dot.Radius - 6 * scale), 5 * scale, reversed, UiTokens.Cyan with { A = dot.Alpha });
            if (dot.Numbers is { } numbers)
                Number(canvas, dot.At + new Vector2(dot.Radius + 4 * scale, dot.Radius + 4 * scale), numbers, scale, dot.Alpha);
        }
    }

    // An order number in a small dark tag below and right of a node.
    private static void Number(Control canvas, Vector2 corner, string text, float scale, float alpha)
    {
        var font = canvas.GetThemeDefaultFont();
        var size = Mathf.RoundToInt(13 * scale);
        var extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var pad = new Vector2(4, 1) * scale;
        canvas.DrawRect(new Rect2(corner, extent + pad * 2), UiTokens.DeepNavy with { A = 0.8f * alpha });
        canvas.DrawString(font, corner + pad + new Vector2(0, font.GetAscent(size)), text, HorizontalAlignment.Left, -1, size, UiTokens.Text with { A = alpha });
    }

    // A chevron at at, pointing along direction.
    private static void Chevron(Control canvas, Vector2 at, Vector2 direction, float size, Color colour)
    {
        var side = new Vector2(-direction.Y, direction.X);
        var tip = at + direction * size * 0.6f;
        canvas.DrawPolyline([at - direction * size * 0.6f + side * size, tip, at - direction * size * 0.6f - side * size], colour, Math.Max(1.5f, size / 2.5f), antialiased: true);
    }

    // Three quarters of a circle with an arrowhead: clockwise on screen when forward, the other way when reversed.
    private static void CurvedArrow(Control canvas, Vector2 centre, float radius, bool reversed, Color colour)
    {
        const float start = -Mathf.Pi / 2, sweep = 1.5f * Mathf.Pi;
        var end = reversed ? start - sweep : start + sweep;
        canvas.DrawArc(centre, radius, start, end, 16, colour, Math.Max(1.5f, radius / 3), antialiased: true);
        var tip = centre + new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * radius;
        var along = new Vector2(-Mathf.Sin(end), Mathf.Cos(end)) * (reversed ? -1 : 1);
        Chevron(canvas, tip, along, radius * 0.7f, colour);
    }

    /// <summary>A control that draws with a callback.</summary>
    private sealed partial class Painter : Control
    {
        public Action<Control>? Paints { get; init; }

        public override void _Draw() => Paints?.Invoke(this);
    }
}
