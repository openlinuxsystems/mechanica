using System.Numerics;

namespace Mechanica.Core.Characters;

/// <summary>
/// Where the water bucket sits in the hand at a moment: how it is held (by the rim or the handle), how far it is
/// swung forward about its hinges, and how much water it holds.
/// </summary>
/// <param name="Grip">0 holds the back of the rim (empty, handle lowered), 1 the handle (full, handle raised).</param>
/// <param name="Tilt">Degrees the bucket swings forward about its hinges, the hand keeping the handle.</param>
/// <param name="Water">0 empty, 1 full.</param>
public readonly record struct BucketState(float Grip, float Tilt, float Water)
{
    /// <summary>An empty bucket held by the rim.</summary>
    public static BucketState Empty => new(0f, 0f, 0f);

    /// <summary>A full bucket carried by the handle.</summary>
    public static BucketState Full => new(1f, 0f, 1f);

    /// <summary>The state <paramref name="u"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static BucketState Lerp(BucketState a, BucketState b, float u) =>
        new(a.Grip + (b.Grip - a.Grip) * u, a.Tilt + (b.Tilt - a.Tilt) * u, a.Water + (b.Water - a.Water) * u);
}

/// <summary>
/// The bucket's body in the frame of the fist holding it, in Y-up model axes (the hand-tools model's own: +Y up, the
/// bucket's front towards +Z when it hangs from the handle): <see cref="X"/>, <see cref="Y"/> and <see cref="Z"/> are
/// its axes (Y up its axis) and <see cref="Origin"/> the middle of its base.
/// </summary>
public readonly record struct BucketTransform(Vector3 X, Vector3 Y, Vector3 Z, Vector3 Origin);

/// <summary>
/// How the held water bucket moves with the arms: the numbers in <see cref="BucketSettings"/>, worked the same way as
/// <c>tools/characters/build_characters.py</c> (<c>bucket_motion</c>, <c>bucket_in_grip</c>, <c>handle_angle</c>), so the
/// bucket the game draws stays in the hand the clips were posed with.
/// </summary>
public static class BucketRig
{
    /// <summary>
    /// The bucket's state <paramref name="seconds"/> into <paramref name="clip"/> (a first-person <c>_fp</c> clip moves
    /// it as its third-person one does), or null when the clip does not move the bucket.
    /// </summary>
    public static BucketState? Motion(BucketSettings settings, string clip, float seconds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.EndsWith(CharacterRig.FirstPersonSuffix, StringComparison.Ordinal))
            clip = clip[..^CharacterRig.FirstPersonSuffix.Length];
        if (!settings.Motion.TryGetValue(clip, out var keys) || keys.Length == 0)
            return null;
        if (seconds <= keys[0][0])
            return State(keys[0]);
        for (var i = 1; i < keys.Length; i++)
        {
            if (seconds <= keys[i][0])
            {
                var u = SmoothStep((seconds - keys[i - 1][0]) / MathF.Max(keys[i][0] - keys[i - 1][0], 1e-6f));
                return BucketState.Lerp(State(keys[i - 1]), State(keys[i]), u);
            }
        }
        return State(keys[^1]);
    }

    /// <summary>How long <paramref name="clip"/> moves the bucket for: its last key's time (0 for a clip that doesn't).</summary>
    public static float Length(BucketSettings settings, string clip)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Motion.TryGetValue(clip, out var keys) && keys.Length > 0 ? keys[^1][0] : 0f;
    }

    /// <summary>The bucket held as <paramref name="prop"/> shows it when no bucket clip is moving it.</summary>
    public static BucketState Resting(CharacterProp prop) =>
        prop is CharacterProp.WaterBucketFull or CharacterProp.ChemicalBucketFull ? BucketState.Full : BucketState.Empty;

    /// <summary>The handle's angle from upright about the bucket's X axis, in degrees (positive falls to the front).</summary>
    public static float HandleDegrees(BucketSettings settings, BucketState state)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.HandleLoweredDegrees * (1f - SmoothStep(state.Grip)) + state.Tilt;
    }

    /// <summary>The height of the water's surface above the base, in the bucket's frame.</summary>
    public static float WaterHeight(BucketSettings settings, BucketState state)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.FloorHeight + (settings.WaterHeight - settings.FloorHeight) * Math.Clamp(state.Water, 0f, 1f);
    }

    /// <summary>The inside's radius at <paramref name="height"/> above the base (the wall tapers out to the rim).</summary>
    public static float InnerRadius(BucketSettings settings, float height)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var u = (height - settings.FloorHeight) / (settings.RimHeight - settings.FloorHeight);
        return settings.InnerRadiusFloor + (settings.InnerRadiusRim - settings.InnerRadiusFloor) * u;
    }

    /// <summary>Where the bucket's body sits in the fist's grip frame for <paramref name="state"/>.</summary>
    public static BucketTransform InGrip(BucketSettings settings, BucketState state)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // Worked in the modelling tool's Z-up axes (grip frame: X along the fingers, Y the thumb), then turned to Y-up.
        // The bucket's X, Y and Z in grip axes when hanging straight.
        var bx = new Vector3(0f, -1f, 0f);
        var by = new Vector3(0f, 0f, 1f);
        var bz = new Vector3(-1f, 0f, 0f);
        Vector3 R0(Vector3 v) => bx * v.X + by * v.Y + bz * v.Z;
        var rim = new Vector3(settings.RimGrip[0], settings.RimGrip[1], settings.RimGrip[2]);
        var oRim = -R0(rim);
        var oHandle = -R0(new Vector3(0f, 0f, settings.HingeHeight + settings.HandleLength));
        var o = Vector3.Lerp(oRim, oHandle, state.Grip);
        var pivot = o + R0(new Vector3(0f, 0f, settings.HingeHeight));
        // Swinging forward turns the bucket about its own X axis, the mouth towards its back.
        var turn = Quaternion.CreateFromAxisAngle(bx, -state.Tilt * MathF.PI / 180f);
        var x = Vector3.Transform(bx, turn);
        var y = Vector3.Transform(by, turn);
        var z = Vector3.Transform(bz, turn);
        var origin = pivot + Vector3.Transform(o - pivot, turn);
        return new BucketTransform(YUp(x), YUp(z), -YUp(y), YUp(origin));
    }

    // Z-up to Y-up (the glTF export): (x, y, z) -> (x, z, -y).
    private static Vector3 YUp(Vector3 v) => new(v.X, v.Z, -v.Y);

    private static BucketState State(float[] key) => new(key[1], key[2], key[3]);

    private static float SmoothStep(float x)
    {
        var t = Math.Clamp(x, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
