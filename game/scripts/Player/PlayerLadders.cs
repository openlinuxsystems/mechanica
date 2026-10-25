using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Net;
using Mechanica.Core.Player;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Player;

// Ladder climbing (Parts map E2): the ladders round the player are the climbable parts (PartDefinition.Climbable) of the
// Creations it touches; the maths is Core's LadderClimb, so the host and a predicting client climb alike. A ladder on a
// moving Creation carries its climber as the floor of one does: the ladder's Body is the Ground while climbing.
public partial class PlayerController
{
    // The ladder held: the Body and part. Null off ladders.
    private (CreationBody Body, int Part)? _ladder;
    private double _sinceLetGo = double.MaxValue;
    private readonly CapsuleShape3D _reachShape = new();
    // Which way a remote climber's body faces, found from the ladder it is on when a snapshot says it climbs.
    private float? _shownClimbYaw;

    /// <summary>True while the player is on a ladder.</summary>
    public bool Climbing => _ladder is not null || (!Simulated && Body.HasFlag(BodyFlags.Climbing));

    /// <summary>The Creation and part of the ladder the player climbs, or null.</summary>
    public (int Creation, int Part)? ClimbingOn => _ladder is { } held && IsInstanceValid(held.Body) ? (held.Body.CreationId, held.Part) : null;

    /// <summary>
    /// Which way a climbing body faces, toward the ladder's face (radians, like <see cref="Node3D.Rotation"/>'s Y), whichever
    /// way its player looks; null off ladders.
    /// </summary>
    public float? ClimbYaw
    {
        get
        {
            if (!Climbing)
                return null;
            if (_ladder is { } held && FrameOf(held) is { } frame)
                return FacingYaw(frame, GlobalPosition);
            return _shownClimbYaw;
        }
    }

    // The yaw that faces the ladder's face in front of feet (forward is -Z, so facing -n is yaw atan2(n.x, n.z)).
    private static float? FacingYaw(LadderFrame frame, Vector3 feet)
    {
        var n = frame.FaceToward(feet.ToNumerics());
        return new Vector2(n.X, n.Z).LengthSquared() > 1e-4f ? Mathf.Atan2(n.X, n.Z) : null;
    }

    // One tick on or onto a ladder. True when it moved the player (the walk is skipped); false to walk or swim as usual.
    private bool StepLadder(PlayerInput input, float delta, bool wasOnFloor, bool swimming)
    {
        _sinceLetGo += delta;
        if (_ladder is null)
        {
            if (_dead || swimming || _crouched || _sinceLetGo < Settings.ClimbRegrabSeconds || !TryGrab(input, wasOnFloor))
                return false;
        }
        var held = _ladder!.Value;
        if (FrameOf(held) is not { } frame)
        {
            LetGo();
            return false;
        }
        var feet = GlobalPosition.ToNumerics();
        var step = LadderClimb.Step(frame, feet, input, Settings, wasOnFloor, FloorBehindTop(frame));
        if (step.Result != ClimbResult.Climbing)
        {
            LetGo();
            if (step.Result == ClimbResult.LetGo)
                return false;
        }
        Velocity = step.Velocity.ToGodot();
        MoveAndSlide();
        if (_ladder is not null)
        {
            // The ladder's Body carries the climber, as a floor does.
            _groundBody = held.Body;
            _offGround = 0;
            Ground = new PlayerGround(held.Body.CreationId, (ushort)held.Body.BodyIndex, (held.Body.GlobalTransform.AffineInverse() * GlobalPosition).ToNumerics());
            Body = BodyFlags.Climbing | (input.Guard ? BodyFlags.Guard : 0);
        }
        else
        {
            // Off the top or jumped off: it still rides the Body for a moment, as after a jump from a deck.
            Ground = Riding(delta);
            Body = IsOnFloor() ? BodyFlags.OnFloor : 0;
        }
        StepEyeHeight(delta);
        return true;
    }

    private void LetGo()
    {
        _ladder = null;
        _sinceLetGo = 0;
    }

    // Grabs a ladder the player walks into, or one whose top it walks onto from the floor behind it.
    private bool TryGrab(PlayerInput input, bool onFloor)
    {
        var wish = PlayerMotor.WishDirection(input.MoveX, input.MoveZ, input.Yaw);
        if (wish.LengthSquared() < 0.01f)
            return false;
        var feet = GlobalPosition.ToNumerics();
        foreach (var (body, part, frame) in LaddersNear())
        {
            if (LadderClimb.CanGrab(frame, feet, wish, Settings))
            {
                _ladder = (body, part);
                return true;
            }
            if (onFloor && LadderClimb.MountFromTop(frame, feet, wish, Settings) is { } mount && RoomAt(mount.ToGodot()))
            {
                GlobalPosition = mount.ToGodot();
                Velocity = Vector3.Zero;
                _ladder = (body, part);
                return true;
            }
        }
        return false;
    }

    // The climbable parts whose Bodies the player is within reach of, nearest first.
    private List<(CreationBody Body, int Part, LadderFrame Frame)> LaddersNear()
    {
        var reach = Settings.ClimbReach + 0.1f;
        _reachShape.Radius = Settings.Radius + reach;
        _reachShape.Height = Settings.Height + 2f * reach;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _reachShape,
            Transform = new Transform3D(Basis.Identity, GlobalPosition + new Vector3(0, Settings.Height / 2f, 0)),
            CollisionMask = CollisionMask,
            Exclude = [GetRid()],
        };
        var found = new List<(CreationBody, int, LadderFrame)>();
        var seen = new HashSet<(ulong, int)>();
        foreach (var hit in GetWorld3D().DirectSpaceState.IntersectShape(query, 32))
        {
            if (hit["collider"].AsGodotObject() is not CreationBody body || PartOf(body, hit["shape"].AsInt32()) is not { } part
                || !seen.Add((body.GetInstanceId(), part)))
                continue;
            if (FrameOf((body, part)) is { } frame)
                found.Add((body, part, frame));
        }
        var feet = GlobalPosition.ToNumerics();
        found.Sort((a, b) => System.Numerics.Vector3.DistanceSquared(a.Item3.Centre, feet).CompareTo(System.Numerics.Vector3.DistanceSquared(b.Item3.Centre, feet)));
        return found;
    }

    // The part id behind one of a Body's shapes (they are named Shape<part id>), or null.
    private static int? PartOf(CreationBody body, int shape)
    {
        if (shape < 0)
            return null;
        var owner = body.ShapeFindOwner(shape);
        return body.ShapeOwnerGetOwner(owner) is CollisionShape3D { Name: var name } ? CreationNode.PartOfShape(name.ToString()) : null;
    }

    // Where a climbable part stands now, or null when it is gone, not climbable or not upright enough.
    private static LadderFrame? FrameOf((CreationBody Body, int Part) ladder)
    {
        var (body, partId) = ladder;
        if (!IsInstanceValid(body) || !body.IsInsideTree() || body.GetParent() is not CreationNode node)
            return null;
        var creation = node.Creation;
        if (creation.FindPart(partId) is not { } part || !body.PartIds.Contains(partId))
            return null;
        var definition = creation.DefinitionOf(partId);
        if (definition.Climbable != true)
            return null;
        var placed = body.GlobalTransform * new Transform3D(part.Rotation.ToBasis(), PartGeometry.CentreMetres(definition, part.Cell, part.Rotation).ToGodot());
        var half = (definition.Size ?? Core.Data.GridSize.One).Metres.ToGodot() / 2f;
        return LadderFrame.FromBox(placed.Origin.ToNumerics(), (placed.Basis.X * half.X).ToNumerics(), (placed.Basis.Y * half.Y).ToNumerics(),
            (placed.Basis.Z * half.Z).ToNumerics());
    }

    // True when there is floor to step onto just behind the ladder's top (a deck it leans on), so climbing on tops out.
    private bool FloorBehindTop(LadderFrame frame)
    {
        var n = frame.FaceToward(GlobalPosition.ToNumerics()).ToGodot();
        var flat = new Vector3(n.X, 0, n.Z);
        if (flat.LengthSquared() < 1e-6f)
            return false;
        var behind = frame.Top.ToGodot() - flat.Normalized() * (frame.HalfDepth + Settings.Radius + 0.1f);
        var ray = PhysicsRayQueryParameters3D.Create(behind + Vector3.Up * 0.6f, behind + Vector3.Down * 0.4f, CollisionMask, [GetRid()]);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        return hit.Count > 0 && hit["normal"].AsVector3().Y >= Mathf.Cos(FloorMaxAngle);
    }

    // A player this peer does not simulate, shown climbing: its body faces the ladder nearest it.
    private void ShowClimbing(bool climbing)
    {
        _shownClimbYaw = null;
        if (!climbing || !IsInsideTree())
            return;
        foreach (var (_, _, frame) in LaddersNear())
        {
            _shownClimbYaw = FacingYaw(frame, GlobalPosition);
            return;
        }
    }
}
