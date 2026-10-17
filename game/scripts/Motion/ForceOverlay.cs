using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Input;
using Mechanica.Core.Motion;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Motion;

/// <summary>
/// The aero force overlay (chunk 11, spec §13.1's debug overlay for aero forces): for Creations near the
/// local player it draws every push a part script made in the host's last physics step (a thruster's
/// thrust, a wing's lift and drag, a rotor's pull) as an arrow from the part along the force, and a
/// rotor's counter-torque as a line along its axis. The pushes are read with the <c>GetForces</c> query a
/// few times a second, so a client sees what the host applies; the arrows sit on the parts as this peer
/// shows them. Shown while <see cref="Enabled"/> (F9 in game, "Show forces" in the Test Sandbox).
/// </summary>
public partial class ForceOverlay : MeshInstance3D
{
    /// <summary>How far from the player Creations are drawn, in m.</summary>
    public const float Range = 60f;

    /// <summary>Seconds between reads.</summary>
    public const double PollSeconds = 0.1;

    /// <summary>Arrow length per newton, in m (100 N is 1 m).</summary>
    public const float MetresPerNewton = 0.01f;

    /// <summary>Line length per newton-metre of torque, in m.</summary>
    public const float MetresPerNewtonMetre = 0.02f;

    /// <summary>Longest arrow, in m, so a big push stays on screen.</summary>
    public const float MaxLength = 6f;

    private static readonly Color ForceColor = UiTokens.ForceArrow;
    private static readonly Color TorqueColor = UiTokens.TorqueArrow;

    private readonly ImmediateMesh _lines = new();
    private readonly Dictionary<int, IReadOnlyList<PushState>> _pushes = [];
    private readonly HashSet<int> _asking = [];
    private double _sincePoll;

    /// <summary>The Creations to draw. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Players, for the local player's position. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Where the reads go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>False in automated tests: F9 does nothing.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>True while the overlay shows.</summary>
    public bool Enabled { get; set; }

    /// <summary>Pushes drawn on the last redraw (Creation, part, world force in N), for tests.</summary>
    public IReadOnlyList<(int Creation, int? Part, Vector3 Force)> Drawn { get; private set; } = [];

    public override void _Ready()
    {
        Name = "ForceOverlay";
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
        if (!ReadDeviceInput || !GameInput.Pressed(@event, InputActions.ForceOverlay))
            return;
        Enabled = !Enabled;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        Visible = Enabled;
        if (!Visible)
        {
            Drawn = [];
            _pushes.Clear();
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

    /// <summary>Reads a Creation's pushes now (the answer arrives through the Action API).</summary>
    public void Poll(int creationId)
    {
        if (!_asking.Add(creationId))
            return;
        Actions.Request(MotionActions.GetForces, new GetForcesArgs(creationId), result =>
        {
            _asking.Remove(creationId);
            if (result is not { Ok: true, Value: { } value })
            {
                _pushes.Remove(creationId);
                return;
            }
            _pushes[creationId] = value.Deserialize<List<PushState>>(ActionJson.Options) ?? [];
        });
    }

    private IEnumerable<CreationNode> Nearby()
    {
        var centre = Players.LocalPlayer?.GlobalPosition;
        foreach (var creation in Creations.All)
        {
            if (Creations.NodeOf(creation.Id) is not { } node || node.Bodies.Count == 0)
                continue;
            if (centre is { } at && node.Bodies[0].GlobalPosition.DistanceTo(at) > Range)
                continue;
            yield return node;
        }
    }

    private void Redraw(IReadOnlyList<CreationNode> nodes)
    {
        _lines.ClearSurfaces();
        var drawn = new List<(int, int?, Vector3)>();
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
        void Arrow(Vector3 from, Vector3 vector, Color color)
        {
            var length = vector.Length();
            if (length < 1e-3f)
                return;
            var tip = from + vector / length * MathF.Min(length, MaxLength);
            Line(from, tip, color);
            var back = (from - tip).Normalized() * 0.15f;
            var side = back.Cross(Vector3.Up).LengthSquared() > 1e-6f ? back.Cross(Vector3.Up).Normalized() * 0.07f : new Vector3(0.07f, 0, 0);
            Line(tip, tip + back + side, color);
            Line(tip, tip + back - side, color);
        }
        foreach (var node in nodes)
        {
            if (!_pushes.TryGetValue(node.Creation.Id, out var pushes))
                continue;
            foreach (var push in pushes)
            {
                if (Origin(node, push) is not { } from)
                    continue;
                var force = new Vector3(push.Force.X, push.Force.Y, push.Force.Z);
                var torque = new Vector3(push.Torque.X, push.Torque.Y, push.Torque.Z);
                Arrow(from, force * MetresPerNewton, ForceColor);
                Arrow(from, torque * MetresPerNewtonMetre, TorqueColor);
                drawn.Add((node.Creation.Id, push.Part, force));
            }
        }
        if (begun)
            _lines.SurfaceEnd();
        Drawn = drawn;
    }

    // Where this peer shows the push: on its part, or at its Body's centre of mass.
    private static Vector3? Origin(CreationNode node, PushState push)
    {
        var creation = node.Creation;
        if (push.Body >= node.Bodies.Count)
            return null;
        var body = node.Bodies[push.Body];
        if (push.Part is { } partId && creation.FindPart(partId) is { } part)
            return body.GlobalTransform * PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToNumerics().ToGodot();
        if (push.Body >= creation.Bodies.Count)
            return null;
        return body.GlobalTransform * creation.Bodies[push.Body].CentreOfMass.ToNumerics().ToGodot();
    }
}
