using System.Numerics;

namespace Mechanica.Core.Player;

/// <summary>
/// A climbable part (<see cref="Data.PartDefinition.Climbable"/>) where it stands this tick, in world metres: its box
/// as a centre and three unit axes with half sizes. <see cref="Up"/> is the long axis the player climbs along (pointing up),
/// <see cref="Normal"/> the thin axis out of its broad faces and <see cref="Across"/> the rungs' direction.
/// </summary>
/// <param name="Centre">Centre of the part's box.</param>
/// <param name="Up">Unit axis climbed along, with a positive Y.</param>
/// <param name="Across">Unit axis along the rungs.</param>
/// <param name="Normal">Unit axis out of the broad faces (either face: <see cref="FaceToward"/> picks the player's).</param>
/// <param name="HalfHeight">Half the length along <see cref="Up"/>.</param>
/// <param name="HalfWidth">Half the length along <see cref="Across"/>.</param>
/// <param name="HalfDepth">Half the thickness along <see cref="Normal"/>.</param>
public readonly record struct LadderFrame(Vector3 Centre, Vector3 Up, Vector3 Across, Vector3 Normal, float HalfHeight, float HalfWidth, float HalfDepth)
{
    /// <summary>The top end of the climbed axis.</summary>
    public Vector3 Top => Centre + Up * HalfHeight;

    /// <summary>The bottom end of the climbed axis.</summary>
    public Vector3 Bottom => Centre - Up * HalfHeight;

    /// <summary>
    /// The ladder of a box given as its centre and three half-extent vectors (each axis scaled by half its size), or null
    /// when its long axis does not stand within <see cref="LadderClimb.MaxTiltDegrees"/> of upright (a ladder laid down is
    /// no ladder). The long axis is climbed (of equally long ones, the most upright); of the other two, the thinner is the depth.
    /// </summary>
    public static LadderFrame? FromBox(Vector3 centre, Vector3 halfX, Vector3 halfY, Vector3 halfZ)
    {
        Span<Vector3> axes = [halfX, halfY, halfZ];
        var up = -1;
        var (longest, best) = (0f, 0f);
        for (var i = 0; i < 3; i++)
        {
            var length = axes[i].Length();
            if (!(length > 1e-5f) || !float.IsFinite(length))
                return null;
            var upright = MathF.Abs(axes[i].Y) / length;
            if (length > longest + 1e-3f || (length > longest - 1e-3f && upright > best))
                (longest, best, up) = (MathF.Max(length, longest), upright, i);
        }
        if (up < 0 || best < MathF.Cos(LadderClimb.MaxTiltDegrees * MathF.PI / 180f))
            return null;
        var a = axes[(up + 1) % 3];
        var b = axes[(up + 2) % 3];
        var (across, depth) = a.LengthSquared() >= b.LengthSquared() ? (a, b) : (b, a);
        var upAxis = axes[up];
        if (upAxis.Y < 0f)
            upAxis = -upAxis;
        return new LadderFrame(centre, Vector3.Normalize(upAxis), Vector3.Normalize(across), Vector3.Normalize(depth),
            upAxis.Length(), across.Length(), depth.Length());
    }

    /// <summary>The face normal on the side of <paramref name="point"/>: out of the face it is in front of.</summary>
    public Vector3 FaceToward(Vector3 point) => Vector3.Dot(point - Centre, Normal) >= 0f ? Normal : -Normal;
}

/// <summary>What one climbing step did.</summary>
public enum ClimbResult
{
    /// <summary>Still on the ladder.</summary>
    Climbing,

    /// <summary>Let go: stepped off at the bottom, or drifted off the ladder. The player walks or falls from here.</summary>
    LetGo,

    /// <summary>Jumped off, away from the face.</summary>
    JumpedOff,

    /// <summary>Climbed over the top onto the floor behind it.</summary>
    TopOut,
}

/// <summary>A climbing step's velocity and what happened.</summary>
/// <param name="Velocity">The velocity to move with this tick, m/s, in the ladder's frame of motion (a moving Creation carries the rest).</param>
/// <param name="Result">Whether the player is still on the ladder.</param>
public readonly record struct ClimbStep(Vector3 Velocity, ClimbResult Result);

/// <summary>
/// Ladder climbing (Parts map E2), engine independent like <see cref="PlayerMotor"/>: whether a player grabs a ladder and
/// how they move on it. The engine finds the ladder parts round the player and their boxes; host and clients run the same
/// maths, so a client's prediction agrees with the host. Forward climbs up and back climbs down whichever way the player
/// looks; strafe moves along the rungs; Jump pushes off. At the top the player steps over onto the floor behind the ladder
/// when there is one, and otherwise hangs with the hands at the top rung.
/// </summary>
public static class LadderClimb
{
    /// <summary>The most a ladder's long axis may lean from upright and still be climbed, in degrees.</summary>
    public const float MaxTiltDegrees = 30f;

    /// <summary>
    /// How far below a ladder's top the feet stop when there is no floor behind it to step onto, in metres: the hands are on
    /// the top rung.
    /// </summary>
    public const float TopHang = 0.9f;

    /// <summary>How far the feet may hang below a ladder's bottom before the player lets go, in metres.</summary>
    public const float BottomReach = 0.5f;

    /// <summary>How close to the face a climber is held, in metres (the gap between the capsule and the face).</summary>
    public const float HoldGap = 0.03f;

    /// <summary>
    /// True when a player at <paramref name="feet"/> walking toward <paramref name="wish"/> (world, horizontal, up to unit
    /// length) grabs <paramref name="ladder"/>: in front of one of its broad faces, within
    /// <see cref="PlayerMovementSettings.ClimbReach"/> of it, below its top and pressing toward it.
    /// </summary>
    public static bool CanGrab(LadderFrame ladder, Vector3 feet, Vector3 wish, PlayerMovementSettings settings)
    {
        var flat = new Vector3(wish.X, 0f, wish.Z);
        if (flat.LengthSquared() < 0.01f || !float.IsFinite(flat.X) || !float.IsFinite(flat.Z))
            return false;
        var n = ladder.FaceToward(feet);
        var inward = Flat(-n);
        if (inward is null || Vector3.Dot(Vector3.Normalize(flat), inward.Value) < 0.5f)
            return false;
        var (gap, lateral, along) = Where(ladder, feet, settings);
        return gap <= settings.ClimbReach && gap >= -settings.Radius
            && MathF.Abs(lateral) <= ladder.HalfWidth + settings.Radius * 0.5f
            && along >= -ladder.HalfHeight - BottomReach && along <= ladder.HalfHeight - 0.3f;
    }

    /// <summary>
    /// Where a player standing on the floor at the top of <paramref name="ladder"/> (behind it, at <paramref name="feet"/>)
    /// and walking toward it (<paramref name="wish"/>) gets onto it to climb down: feet on the far face, a little below the top,
    /// facing it. Null when they are not at its top, not by it or not walking toward it. The engine checks there is room there.
    /// </summary>
    public static Vector3? MountFromTop(LadderFrame ladder, Vector3 feet, Vector3 wish, PlayerMovementSettings settings)
    {
        var flat = new Vector3(wish.X, 0f, wish.Z);
        if (flat.LengthSquared() < 0.01f || !float.IsFinite(flat.X) || !float.IsFinite(flat.Z))
            return null;
        var behind = ladder.FaceToward(feet);
        if (Flat(-behind) is not { } toward || Vector3.Dot(Vector3.Normalize(flat), toward) < 0.5f)
            return null;
        var (gap, lateral, along) = Where(ladder, feet, settings);
        if (gap > settings.ClimbReach || gap < -settings.Radius || MathF.Abs(lateral) > ladder.HalfWidth
            || along < ladder.HalfHeight - 0.35f || along > ladder.HalfHeight + 0.35f)
            return null;
        var front = -behind;
        return ladder.Centre + ladder.Across * Math.Clamp(lateral, -ladder.HalfWidth, ladder.HalfWidth)
            + front * (ladder.HalfDepth + settings.Radius + HoldGap) + ladder.Up * (ladder.HalfHeight - TopHang);
    }

    /// <summary>
    /// One tick on a ladder for a player at <paramref name="feet"/>. <paramref name="onFloor"/> says the feet stand on
    /// something (so climbing down steps off), and <paramref name="floorBehindTop"/> that there is floor to step onto
    /// behind the ladder's top. <paramref name="input"/> is sanitized here.
    /// </summary>
    public static ClimbStep Step(LadderFrame ladder, Vector3 feet, PlayerInput input, PlayerMovementSettings settings, bool onFloor, bool floorBehindTop)
    {
        input = input.Sanitized(settings);
        var n = ladder.FaceToward(feet);
        if (input.Jump)
            return new ClimbStep((Flat(n) ?? Vector3.Zero) * settings.ClimbJumpSpeed + Vector3.UnitY * settings.ClimbJumpUp, ClimbResult.JumpedOff);
        var (gap, lateral, along) = Where(ladder, feet, settings);
        if (gap > 2f * settings.ClimbReach || MathF.Abs(lateral) > ladder.HalfWidth + settings.Radius
            || along < -ladder.HalfHeight - BottomReach)
            return new ClimbStep(Vector3.Zero, ClimbResult.LetGo);
        var climb = input.MoveZ;
        if (climb < -0.1f && onFloor)
            return new ClimbStep(Vector3.Zero, ClimbResult.LetGo);
        if (climb > 0.1f && floorBehindTop && along >= ladder.HalfHeight - 0.02f)
            return new ClimbStep((Flat(-n) ?? Vector3.Zero) * settings.ClimbTopOutSpeed + Vector3.UnitY * (settings.ClimbSpeed * 0.5f), ClimbResult.TopOut);

        var highest = floorBehindTop ? ladder.HalfHeight + 0.05f : ladder.HalfHeight - TopHang;
        var up = climb * settings.ClimbSpeed;
        if (up > 0f && along >= highest)
            up = 0f;
        // Strafe is to the right of a player facing the face.
        var right = Vector3.Cross(-n, ladder.Up);
        right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : ladder.Across;
        var side = input.MoveX * settings.ClimbSideSpeed;
        var sideways = Vector3.Dot(right, ladder.Across) >= 0f ? lateral : -lateral;
        if (side * sideways > 0f && MathF.Abs(lateral) >= ladder.HalfWidth)
            side = 0f;
        // Drawn in to the face, gently, so the capsule rests against it rather than bouncing off.
        var drift = gap - HoldGap;
        var inward = drift > 0f ? MathF.Min(drift * 8f, settings.ClimbSpeed) : 0f;
        return new ClimbStep(ladder.Up * up + right * side - n * inward, ClimbResult.Climbing);
    }

    // The capsule's gap to the face it is in front of (negative: in it), how far it is along the rungs from the centre,
    // and how far along the climbed axis the feet are from the centre.
    private static (float Gap, float Lateral, float Along) Where(LadderFrame ladder, Vector3 feet, PlayerMovementSettings settings)
    {
        var offset = feet - ladder.Centre;
        var outward = MathF.Abs(Vector3.Dot(offset, ladder.Normal));
        return (outward - ladder.HalfDepth - settings.Radius, Vector3.Dot(offset, ladder.Across), Vector3.Dot(offset, ladder.Up));
    }

    private static Vector3? Flat(Vector3 v)
    {
        var flat = new Vector3(v.X, 0f, v.Z);
        return flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : null;
    }
}
