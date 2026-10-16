namespace Mechanica.Core.Player;

/// <summary>
/// One physics tick of player intent. This is the only thing a player controller consumes, so the
/// same struct can come from the local keyboard, a network RPC (host-authoritative, D10) or a bot.
/// </summary>
/// <param name="MoveX">Strafe axis, -1 (left) to 1 (right).</param>
/// <param name="MoveZ">Forward axis, -1 (back) to 1 (forward).</param>
/// <param name="Yaw">Absolute look yaw in radians (rotation about +Y, 0 looks down -Z).</param>
/// <param name="Pitch">Absolute look pitch in radians (positive looks up).</param>
/// <param name="Jump">Jump is held this tick.</param>
/// <param name="Sprint">Sprint is held this tick.</param>
public readonly record struct PlayerInput(
    float MoveX,
    float MoveZ,
    float Yaw,
    float Pitch,
    bool Jump,
    bool Sprint)
{
    /// <summary>No movement, looking straight ahead.</summary>
    public static PlayerInput None => default;

    /// <summary>
    /// Returns a copy that is safe to simulate: non-finite values become 0, the move vector is
    /// limited to length 1, yaw is wrapped to (-π, π] and pitch is clamped to the settings' limit.
    /// The host runs every input through this before using it, whoever sent it.
    /// </summary>
    public PlayerInput Sanitized(PlayerMovementSettings settings)
    {
        var x = Finite(MoveX);
        var z = Finite(MoveZ);
        var lengthSquared = x * x + z * z;
        if (lengthSquared > 1f)
        {
            var length = MathF.Sqrt(lengthSquared);
            x /= length;
            z /= length;
        }

        var maxPitch = settings.MaxPitchDegrees * MathF.PI / 180f;
        return this with
        {
            MoveX = x,
            MoveZ = z,
            Yaw = WrapAngle(Finite(Yaw)),
            Pitch = Math.Clamp(Finite(Pitch), -maxPitch, maxPitch),
        };
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0f;

    private static float WrapAngle(float radians)
    {
        var wrapped = MathF.IEEERemainder(radians, 2f * MathF.PI);
        return wrapped <= -MathF.PI ? wrapped + 2f * MathF.PI : wrapped;
    }
}
