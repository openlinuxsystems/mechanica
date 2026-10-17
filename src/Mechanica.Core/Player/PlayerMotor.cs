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
    /// <param name="speedScale">What walking and running speed are multiplied by (a slow from a bot's bubble wrap), 0 to 1.</param>
    /// <param name="waterDepth">
    /// How far the feet are below the water surface in metres (0 or less: out of water). From
    /// <see cref="PlayerMovementSettings.SwimDepth"/> the player swims: jump rises, crouch dives, and otherwise they
    /// float with their head out.
    /// </param>
    public static Vector3 Step(
        Vector3 velocity,
        bool onFloor,
        PlayerInput input,
        PlayerMovementSettings settings,
        float delta,
        float speedScale = 1f,
        float waterDepth = 0f)
    {
        if (!float.IsFinite(delta) || delta <= 0f)
            return velocity;
        input = input.Sanitized(settings);
        var scale = float.IsFinite(speedScale) ? Math.Clamp(speedScale, 0f, 1f) : 1f;
        if (IsSwimming(waterDepth, settings))
            return Swim(velocity, input, settings, delta, scale, waterDepth);

        var speed = (input.Crawl ? settings.CrawlSpeed : input.Sprint ? settings.SprintSpeed : settings.WalkSpeed) * scale;
        if (float.IsFinite(waterDepth) && waterDepth > 0f)
            speed *= settings.WadeSpeedScale;
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

    /// <summary>True when feet <paramref name="waterDepth"/> metres under the surface swim rather than walk.</summary>
    public static bool IsSwimming(float waterDepth, PlayerMovementSettings settings) =>
        float.IsFinite(waterDepth) && waterDepth >= settings.SwimDepth;

    // In deep water: no gravity, buoyancy holds the player at their float depth, jump rises and crouch dives.
    // At the surface, jump leaps, so a swimmer can climb out onto a low edge.
    private static Vector3 Swim(Vector3 velocity, PlayerInput input, PlayerMovementSettings settings, float delta, float scale, float depth)
    {
        var speed = (input.Sprint ? settings.SwimSprintSpeed : settings.SwimSpeed) * scale;
        var target = WishDirection(input.MoveX, input.MoveZ, input.Yaw) * speed;
        var step = settings.SwimAcceleration * delta;
        var horizontal = MoveToward(new Vector3(velocity.X, 0f, velocity.Z), target, step);

        if (input.Jump && depth <= settings.FloatDepth + 0.1f)
            return new Vector3(horizontal.X, settings.JumpSpeed, horizontal.Z);
        var targetY = input.Jump ? settings.SwimVerticalSpeed
            : input.Crawl ? -settings.SwimVerticalSpeed
            : Math.Clamp((depth - settings.FloatDepth) * settings.Buoyancy, -settings.SwimVerticalSpeed, settings.SwimVerticalSpeed);
        var vertical = velocity.Y + Math.Clamp(targetY - velocity.Y, -step, step);
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
