using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Input;
using Mechanica.Core.Logic;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Logic;

/// <summary>
/// The Logic Visualizer (chunk 10): for Creations near the local player it draws every logic link as a line
/// from the source part to the part it feeds, bright while the source's output is on (not 0) and dim while it
/// is off, with a cross on each source. Signals are read with the <c>InspectCreation</c> query a few times a
/// second, so a client sees what the host's bus does. Shown while <see cref="Enabled"/> (F7 in game) or while
/// the Connect Tool is in hand (<see cref="ShowLinks"/>).
/// </summary>
public partial class LogicOverlay : MeshInstance3D
{
    /// <summary>How far from the player Creations are drawn, in m.</summary>
    public const float Range = 40f;

    /// <summary>Seconds between signal reads.</summary>
    public const double PollSeconds = 0.2;

    private static readonly Color OnColor = new(1f, 0.85f, 0.2f);
    private static readonly Color OffColor = new(0.3f, 0.45f, 0.9f);

    private readonly ImmediateMesh _lines = new();
    private readonly Dictionary<int, IReadOnlyDictionary<int, float>> _signals = [];
    private readonly HashSet<int> _asking = [];
    private double _sincePoll;

    /// <summary>The Creations to draw. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Players, for the local player's position. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Where signal reads go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>False in automated tests: F7 does nothing.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>True when the player turned the visualizer on (F7).</summary>
    public bool Enabled { get; set; }

    /// <summary>True while something else wants links shown (the Connect Tool in hand).</summary>
    public bool ShowLinks { get; set; }

    /// <summary>Asked every frame whether links should show (the Connect Tool in hand); optional.</summary>
    public Func<bool>? Linking { get; init; }

    /// <summary>Links drawn on the last redraw, with whether each was on, for tests.</summary>
    public IReadOnlyList<(int Creation, LogicLink Link, bool On)> Drawn { get; private set; } = [];

    public override void _Ready()
    {
        Name = "LogicOverlay";
        Mesh = _lines;
        TopLevel = true;
        CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
        };
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
        Visible = Enabled || ShowLinks || Linking?.Invoke() == true;
        if (!Visible)
        {
            Drawn = [];
            return;
        }
        var shown = Nearby().ToList();
        _sincePoll += delta;
        if (_sincePoll >= PollSeconds)
        {
            _sincePoll = 0;
            foreach (var node in shown)
                Poll(node.Creation.Id);
        }
        Redraw(shown);
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
            _signals[creationId] = inspection.Parts.Where(p => p.Output != 0).ToDictionary(p => p.Id, p => p.Output);
        });
    }

    /// <summary>The output this peer last read for a part (0 when off or not read yet).</summary>
    public float SignalOf(int creationId, int partId) =>
        _signals.GetValueOrDefault(creationId)?.GetValueOrDefault(partId) ?? 0f;

    private IEnumerable<CreationNode> Nearby()
    {
        var centre = Players.LocalPlayer?.GlobalPosition;
        foreach (var creation in Creations.All)
        {
            if (creation.Links.Count == 0 || Creations.NodeOf(creation.Id) is not { } node || node.Bodies.Count == 0)
                continue;
            if (centre is { } at && node.Bodies[0].GlobalPosition.DistanceTo(at) > Range)
                continue;
            yield return node;
        }
    }

    private void Redraw(IReadOnlyList<CreationNode> nodes)
    {
        _lines.ClearSurfaces();
        var drawn = new List<(int, LogicLink, bool)>();
        var begun = false;
        void Line(Vector3 a, Vector3 b, Color color)
        {
            if (!begun)
                _lines.SurfaceBegin(Godot.Mesh.PrimitiveType.Lines);
            begun = true;
            _lines.SurfaceSetColor(color);
            _lines.SurfaceAddVertex(a);
            _lines.SurfaceSetColor(color);
            _lines.SurfaceAddVertex(b);
        }
        foreach (var node in nodes)
        {
            var creation = node.Creation;
            foreach (var link in creation.Links)
            {
                if (Centre(node, link.From) is not { } from || Centre(node, link.To) is not { } to)
                    continue;
                var on = SignalOf(creation.Id, link.From) != 0;
                var color = on ? OnColor : OffColor;
                Line(from, to, color);
                // An arrowhead near the target shows which way the signal goes.
                var back = (from - to).Normalized() * 0.08f;
                var side = back.Cross(Vector3.Up).LengthSquared() > 1e-6f ? back.Cross(Vector3.Up).Normalized() * 0.04f : new Vector3(0.04f, 0, 0);
                var tip = to + back;
                Line(tip, tip + back + side, color);
                Line(tip, tip + back - side, color);
                drawn.Add((creation.Id, link, on));
            }
        }
        if (begun)
            _lines.SurfaceEnd();
        Drawn = drawn;
    }

    // World centre of a part as this peer shows it.
    private static Vector3? Centre(CreationNode node, int partId)
    {
        var creation = node.Creation;
        if (creation.FindPart(partId) is not { } part)
            return null;
        var index = creation.BodyIndexOf(partId);
        if (index >= node.Bodies.Count)
            return null;
        return node.Bodies[index].GlobalTransform * PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToNumerics().ToGodot();
    }
}
