using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.World;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The Lift in the local player's hand. Aimed at the ground it shows where the platform would go and
/// <see cref="Place"/> puts the player's Lift there (carrying whatever it holds); aimed at a Creation,
/// <see cref="Place"/> slides the Lift under it and picks it up. Keys: left click places or picks up,
/// mouse wheel raises and lowers, R turns the held Creation, right click takes the Lift away. Every
/// change is a <see cref="ToolActions"/> request; the host decides.
/// </summary>
public partial class LiftTool : HandTool
{
    private MeshInstance3D _preview = null!;

    /// <summary>What the Lift would do now.</summary>
    /// <param name="Position">Where the middle of the platform's base would go.</param>
    /// <param name="Creation">The Creation it would pick up, or null to place (or move) the Lift.</param>
    public sealed record Target(Vec3 Position, int? Creation);

    /// <inheritdoc/>
    public override string ToolName => "Lift";

    /// <summary>The current aim, updated every physics frame while in hand (and by <see cref="UpdateAim"/>).</summary>
    public Target? Aim { get; private set; }

    /// <summary>This player's Lift as this peer last saw it, or null.</summary>
    public LiftState? Own => Build.Lifts.TryGetValue(Player.PeerId, out var lift) ? lift.State : null;

    public override void _Ready()
    {
        var size = Build.Settings!.Lift;
        _preview = new MeshInstance3D
        {
            Name = "Preview",
            TopLevel = true,
            Visible = false,
            Mesh = new BoxMesh { Size = new Vector3(size.PlatformSize, size.BaseHeight, size.PlatformSize), Material = HammerTool.PreviewMaterial },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_preview);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Active)
            UpdateAim();
    }

    /// <summary>Casts the aim ray now and moves the preview.</summary>
    public void UpdateAim()
    {
        Aim = Cast();
        _preview.Visible = Active && Aim is { Creation: null };
        if (Aim is not null)
            _preview.GlobalPosition = Aim.Position.ToGodot() + new Vector3(0, Build.Settings!.Lift.BaseHeight / 2, 0);
    }

    /// <summary>Places the Lift or picks up the aimed-at Creation. False when nothing is aimed at.</summary>
    public bool Place(Action<ActionResult>? done = null)
    {
        if (Aim is not { } aim)
            return false;
        Send(ToolActions.PlaceLift, new PlaceLiftArgs(aim.Position, aim.Creation), done);
        return true;
    }

    /// <summary>Raises (positive) or lowers the Lift by <paramref name="steps"/> levels. False without a Lift.</summary>
    public bool Raise(int steps, Action<ActionResult>? done = null)
    {
        if (Own is not { } lift)
            return false;
        var level = Math.Clamp(lift.Level + steps, 0, Build.Settings!.Lift.MaxLevel);
        Send(ToolActions.SetLiftLevel, new SetLiftLevelArgs(level), done);
        return true;
    }

    /// <summary>Turns the held Creation a quarter turn. False without a Lift.</summary>
    public bool Turn(Action<ActionResult>? done = null)
    {
        if (Own is null)
            return false;
        Send(ToolActions.TurnLift, new TurnLiftArgs(1), done);
        return true;
    }

    /// <summary>Takes the Lift away; what it held drops. False without a Lift.</summary>
    public bool Remove(Action<ActionResult>? done = null)
    {
        if (Own is null)
            return false;
        Send(ToolActions.RemoveLift, new NoArgs(), done);
        return true;
    }

    /// <inheritdoc/>
    public override string HudText()
    {
        var lift = Own is { } own ? own.Creation is { } c ? Loc.T("level {0}, holding creation {1}", own.Level, c) : Loc.T("level {0}", own.Level) : Loc.T("not placed");
        return Loc.T("Lift ({0})   {1} place / pick up  {2}/{3} raise/lower  {4} turn  {5} take away", lift, Key(InputActions.Primary), Key(InputActions.LiftUp), Key(InputActions.LiftDown),
            Key(InputActions.Rotate), Key(InputActions.Secondary)) + Status;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            Place();
        else if (Pressed(@event, InputActions.Secondary))
            Remove();
        else if (Pressed(@event, InputActions.LiftUp))
            Raise(1);
        else if (Pressed(@event, InputActions.LiftDown))
            Raise(-1);
        else if (Pressed(@event, InputActions.RotateBack))
        {
            for (var i = 0; i < 3; i++)
                Turn();
        }
        else if (Pressed(@event, InputActions.Rotate))
            Turn();
        else
            return false;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnActiveChanged() => _preview.Visible = Active && Aim is { Creation: null };

    private Target? Cast()
    {
        if (CastRay() is not { } hit || hit.Collider is LiftBody)
            return null;
        if (hit.Node is { } node)
        {
            // Under the Creation: the middle of its box, on whatever is beneath it.
            var creation = node.Creation;
            var box = CreationBounds.World(node.LocalBounds, [.. node.Bodies.Select(b => b.GlobalTransform.ToPose())]);
            var middle = ((box.Min + box.Max) / 2).ToGodot();
            var ground = GroundBelow(new Vector3(middle.X, box.Min.Y, middle.Z), node) ?? box.Min.Y;
            return new Target(new Vec3(middle.X, ground, middle.Z), creation.Id);
        }
        if (hit.Normal.Y < 0.7f)
            return null;
        var snapped = BuildAim.Snap(hit.Point.ToNumerics()).ToGodot();
        return new Target(new Vec3(snapped.X, hit.Point.Y, snapped.Z), null);
    }

    private float? GroundBelow(Vector3 from, Creations.CreationNode skip)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * Build.Settings!.Reach);
        var exclude = new Godot.Collections.Array<Rid>([.. skip.Bodies.Select(b => b.GetRid()), Player.GetRid()]);
        if (Build.Lifts.TryGetValue(Player.PeerId, out var own))
            exclude.Add(own.GetRid());
        query.Exclude = exclude;
        var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : hit["position"].AsVector3().Y;
    }
}
