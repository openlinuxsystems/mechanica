using System.Numerics;

namespace Mechanica.Core.Player;

/// <summary>
/// Engine-independent on-foot movement. Given the current velocity, ground contact and one tick of
/// input, it returns the velocity the character body should move with. Collision response stays in
/// the engine; this only decides intent, so host and clients compute the same result.
/// </summary>
public static class PlayerMotor
{
    /// <summary>
    /// World-space unit direction for a move input at a yaw. Yaw 0 faces -Z (Godot's forward),
    /// positive yaw turns left (counter-clockwise seen from above), +X is right at yaw 0.
    /// </summary>
    public static Vector3 WishDirection(float moveX, float moveZ, float yaw)
    {
        var (sin, cos) = MathF.SinCos(yaw);
        var forward = new Vector3(-sin, 0f, -cos);
        var right = new Vector3(cos, 0f, -sin);
        var wish = forward * moveZ + right * moveX;
        var length = wish.Length();
        return length > 1f ? wish / length : wish;
    }

    /// <summary>Computes the next velocity for one physics step.</summary>
    /// <param name="velocity">Current velocity in m/s.</param>
    /// <param name="onFloor">Whether the body is standing on something this step.</param>
    /// <param name="input">Input for this tick. It is sanitized here, so raw network input is safe.</param>
    /// <param name="settings">Movement tuning.</param>
    /// <param name="delta">Step length in seconds.</param>
    public static Vector3 Step(
        Vector3 velocity,
        bool onFloor,
        PlayerInput input,
        PlayerMovementSettings settings,
        float delta)
    {
        if (!float.IsFinite(delta) || delta <= 0f)
            return velocity;
        input = input.Sanitized(settings);

        var speed = input.Sprint ? settings.SprintSpeed : settings.WalkSpeed;
        var target = WishDirection(input.MoveX, input.MoveZ, input.Yaw) * speed;
        var acceleration = onFloor ? settings.GroundAcceleration : settings.AirAcceleration;

        var horizontal = new Vector3(velocity.X, 0f, velocity.Z);
        horizontal = MoveToward(horizontal, target, acceleration * delta);

        var vertical = velocity.Y;
        if (onFloor)
            vertical = input.Jump ? settings.JumpSpeed : MathF.Min(vertical, 0f);
        else
            vertical = MathF.Max(vertical - settings.Gravity * delta, -settings.MaxFallSpeed);

        return new Vector3(horizontal.X, vertical, horizontal.Z);
    }

    /// <summary>Moves <paramref name="from"/> toward <paramref name="to"/> by at most <paramref name="maxDelta"/>.</summary>
    public static Vector3 MoveToward(Vector3 from, Vector3 to, float maxDelta)
    {
        var difference = to - from;
        var distance = difference.Length();
        return distance <= maxDelta || distance == 0f ? to : from + difference / distance * maxDelta;
    }
}
