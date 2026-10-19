using Mechanica.Core.Characters;

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
/// <param name="Crawl">Crouch is held this tick (in deep water it dives).</param>
/// <param name="Prop">What the player holds, for how others see them. It does not change movement.</param>
/// <param name="Gesture">The player's newest tool or item gesture, for how others see them.</param>
/// <param name="GestureCount">Goes up by one with every gesture, so a repeat is still seen as new.</param>
/// <param name="Guard">
/// The tool in hand is raised, for how others see them: the Sledgehammer held up to block, the Connect Tool aimed at a
/// node, the Paint Tool spraying (<see cref="CharacterRig.CanGuard"/>).
/// </param>
/// <param name="Tint">
/// The Paint Tool's colour, for how others see its tubes and its spray: the palette index, plus
/// <see cref="CharacterRig.TintErase"/> while it takes paint off.
/// </param>
/// <param name="Held">The item in hand that has a held model (<see cref="HeldItems"/>), for how others see them; 0 for none.</param>
public readonly record struct PlayerInput(
    float MoveX,
    float MoveZ,
    float Yaw,
    float Pitch,
    bool Jump,
    bool Sprint,
    bool Crawl = false,
    CharacterProp Prop = CharacterProp.None,
    CharacterGesture Gesture = CharacterGesture.None,
    byte GestureCount = 0,
    bool Guard = false,
    byte Tint = 0,
    ushort Held = 0)
{
    /// <summary>No movement, looking straight ahead.</summary>
    public static PlayerInput None => default;

    /// <summary>
    /// Returns a copy that is safe to simulate: non-finite values become 0, the move vector is
    /// limited to length 1, yaw is wrapped to (-π, π], pitch is clamped to the settings' limit and unknown
    /// props and gestures become none.
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
            Prop = CharacterRig.Sanitize(Prop),
            Gesture = CharacterRig.Sanitize(Gesture),
            Guard = Guard && CharacterRig.CanGuard(CharacterRig.Sanitize(Prop)),
        };
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0f;

    private static float WrapAngle(float radians)
    {
        var wrapped = MathF.IEEERemainder(radians, 2f * MathF.PI);
        return wrapped <= -MathF.PI ? wrapped + 2f * MathF.PI : wrapped;
    }
}
