using Godot;
using Mechanica.Core.Characters;

namespace Mechanica.Game.Player;

/// <summary>
/// The water bucket in a character's hand, moved every frame the way the bucket clips were posed (<see cref="BucketRig"/>):
/// the body hangs from the rim or the handle and swings forward when water is thrown, the handle falls and rises, the
/// water rises as it fills and drains as it is thrown, with drips and splashes at the mouth. Everything follows from the
/// animator, so every peer draws the same bucket with no traffic of its own.
/// </summary>
public sealed class HeldBucket
{
    // How fast the drawn bucket closes on where the clips put it (per second): fast enough to follow a throw, slow
    // enough that the handle swings rather than jumps when the prop changes.
    private const float FollowRate = 14f;
    private const float HandleSpring = 120f;
    private const float HandleDamping = 14f;

    private readonly BucketSettings _settings;
    private readonly Node3D _body;
    private readonly Node3D? _handle;
    private readonly Node3D? _water;
    private readonly CpuParticles3D _drops;
    private BucketState _shown;
    private float _handleAngle;
    private float _handleSpeed;
    private bool _started;

    private HeldBucket(BucketSettings settings, Node3D body, Node3D? handle, Node3D? water)
    {
        _settings = settings;
        _body = body;
        _handle = handle;
        _water = water;
        _drops = new CpuParticles3D
        {
            Name = "Drops",
            Emitting = false,
            Amount = 48,
            Lifetime = 0.7,
            Explosiveness = 0.2f,
            Direction = Vector3.Up,
            Spread = 50f,
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 1.4f,
            Gravity = new Vector3(0, -9.8f, 0),
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = settings.InnerRadiusRim,
            EmissionRingInnerRadius = settings.InnerRadiusRim * 0.6f,
            EmissionRingHeight = 0.01f,
            Position = new Vector3(0, settings.RimHeight, 0),
            Mesh = new SphereMesh { Radius = 0.012f, Height = 0.024f, RadialSegments = 6, Rings = 3 },
            MaterialOverride = WaterDropMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _body.AddChild(_drops);
    }

    /// <summary>The bucket under the prop node <paramref name="prop"/>, or null when it has none.</summary>
    public static HeldBucket? Find(Node3D prop, BucketSettings settings)
    {
        if (prop.FindChild("water_bucket_body", recursive: true, owned: false) is not Node3D body)
            return null;
        return new HeldBucket(settings, body,
            body.FindChild("water_bucket_body_handle", recursive: false, owned: false) as Node3D,
            body.FindChild("water_bucket_body_water", recursive: false, owned: false) as Node3D);
    }

    /// <summary>
    /// Where the clips put the bucket this frame: the gesture playing (if it moves the bucket) blended over how
    /// <paramref name="prop"/> rests by the gesture's weight.
    /// </summary>
    public static BucketState Target(BucketSettings settings, CharacterProp prop, CharacterAnimator animator)
    {
        var rest = BucketRig.Resting(prop);
        if (animator.Gesture is not { } gesture || BucketRig.Motion(settings, gesture, animator.GestureTime) is not { } moving)
            return rest;
        return BucketState.Lerp(rest, moving, animator.GestureWeight);
    }

    /// <summary>Moves the bucket towards <paramref name="target"/>; <paramref name="filling"/> runs as water starts to pour in.</summary>
    public void Update(BucketState target, float delta, Action? filling = null)
    {
        if (!_started)
        {
            (_shown, _handleAngle, _started) = (target, BucketRig.HandleDegrees(_settings, target), true);
        }
        var u = 1f - Mathf.Exp(-FollowRate * delta);
        var was = _shown;
        _shown = BucketState.Lerp(_shown, target, u);
        // Big turns (a throw) are followed directly: smoothing them would lag the bucket behind the hand.
        if (Mathf.Abs(target.Tilt - _shown.Tilt) > 25f)
            _shown = _shown with { Tilt = target.Tilt };

        var t = BucketRig.InGrip(_settings, _shown);
        _body.Transform = new Transform3D(new Basis(Godot(t.X), Godot(t.Y), Godot(t.Z)), Godot(t.Origin));

        // The handle swings on its hinges like a pendulum towards where the clips hold it.
        var want = BucketRig.HandleDegrees(_settings, _shown);
        _handleSpeed += ((want - _handleAngle) * HandleSpring - _handleSpeed * HandleDamping) * delta;
        _handleAngle += _handleSpeed * delta;
        if (_handle is not null)
            _handle.Transform = new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(_handleAngle)), new Vector3(0, _settings.HingeHeight, 0));

        if (_water is not null)
        {
            var height = BucketRig.WaterHeight(_settings, _shown);
            var radius = BucketRig.InnerRadius(_settings, height);
            _water.Visible = _shown.Water > 0.02f && Mathf.Abs(_shown.Tilt) < 60f;
            _water.Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(radius, 1f, radius)), new Vector3(0, height, 0));
        }

        // Water splashes up at the mouth while it rushes in, and slops out while it is thrown.
        var rising = _shown.Water - was.Water > 0.04f * delta;
        var spilling = was.Water - _shown.Water > 0.04f * delta;
        _drops.Emitting = rising || spilling;
        if (rising && was.Water < 0.05f && _shown.Water >= 0.05f)
            filling?.Invoke();
    }

    // The Y-up model axes BucketRig works in are the prop file's own.
    private static Vector3 Godot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static StandardMaterial3D WaterDropMaterial() => new()
    {
        AlbedoColor = new Color(0.45f, 0.7f, 0.82f, 0.8f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.1f,
    };
}
