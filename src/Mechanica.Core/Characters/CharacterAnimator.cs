using System.Numerics;

namespace Mechanica.Core.Characters;

/// <summary>What a character is doing this frame, as far as its animation is concerned.</summary>
/// <param name="Velocity">World velocity in m/s.</param>
/// <param name="Yaw">Body yaw in radians (0 faces -Z, positive turns left).</param>
/// <param name="Pitch">Look pitch in radians (positive looks up).</param>
/// <param name="OnFloor">Standing on something.</param>
/// <param name="Swimming">In deep water.</param>
/// <param name="Crawl">Crouching.</param>
/// <param name="Seated">In a seat.</param>
/// <param name="Prop">What is in hand.</param>
/// <param name="Gesture">The newest gesture.</param>
/// <param name="GestureCount">Counts gestures, so a repeat of the same one is still new.</param>
/// <param name="Guard">Holding the Sledgehammer up to block.</param>
public readonly record struct CharacterPose(
    Vector3 Velocity, float Yaw, float Pitch, bool OnFloor, bool Swimming = false, bool Crawl = false, bool Seated = false,
    CharacterProp Prop = CharacterProp.None, CharacterGesture Gesture = CharacterGesture.None, byte GestureCount = 0,
    bool Guard = false);

/// <summary>Something a character did this frame that makes a sound.</summary>
public enum CharacterEvent
{
    /// <summary>The left foot struck the ground.</summary>
    StepLeft,

    /// <summary>The right foot struck the ground.</summary>
    StepRight,

    /// <summary>Left the ground in a jump.</summary>
    Jump,

    /// <summary>Landed.</summary>
    Land,

    /// <summary>Landed from a long fall.</summary>
    LandHard,

    /// <summary>A swimming stroke.</summary>
    SwimStroke,

    /// <summary>Went into deep water.</summary>
    WaterEnter,

    /// <summary>Came out of deep water.</summary>
    WaterExit,

    /// <summary>Took something else in hand.</summary>
    Equip,
}

/// <summary>Broad states of the body, which crossfade into each other.</summary>
public enum CharacterMode
{
    /// <summary>Standing, walking and running.</summary>
    Ground,

    /// <summary>Crouching.</summary>
    Crouch,

    /// <summary>Jumping and falling.</summary>
    Air,

    /// <summary>In deep water.</summary>
    Swim,

    /// <summary>In a seat.</summary>
    Sit,
}

/// <summary>One clip's contribution to a pose.</summary>
/// <param name="Clip">Clip name (<see cref="CharacterRig.Clips"/>).</param>
/// <param name="Time">Where in the clip, in seconds.</param>
/// <param name="Weight">Its share of the layer, 0 to 1.</param>
public readonly record struct ClipWeight(string Clip, float Time, float Weight);

/// <summary>
/// The animation state machine every player character shares. Each frame it takes a <see cref="CharacterPose"/>
/// and says which clips to blend, where and how much: a whole-body layer (moving, crouching, jumping,
/// swimming, sitting) and an upper-body layer (holding a tool, gestures) on <see cref="CharacterRig.UpperBodyBones"/>.
/// Walking and running clips are driven by distance travelled rather than time, so the feet keep step with
/// the ground at any speed and every moving clip strikes on the same beat; footsteps come from that beat.
/// It runs on every peer from replicated state, so it needs no network traffic of its own.
/// </summary>
public sealed class CharacterAnimator
{
    private static readonly CharacterMode[] Modes = Enum.GetValues<CharacterMode>();

    private readonly CharacterSettings _settings;
    private readonly IReadOnlyDictionary<string, float> _lengths;
    private readonly float[] _modeWeights = new float[Modes.Length];
    private readonly Dictionary<string, float> _holds = new(StringComparer.Ordinal);
    private readonly List<ClipWeight> _body = [];
    private readonly List<ClipWeight> _upper = [];
    private readonly List<CharacterEvent> _events = [];
    private readonly (string Clip, float Speed)[] _ground;
    private readonly (string Clip, float Speed)[] _crouch;
    private readonly (string Clip, float Speed)[] _swim;
    private bool _started;
    private float _clock;
    private float _phase;
    private float _swimPhase;
    private float _airTime;
    private bool _jumped;
    private float _peakFall;
    private float _landTime = float.PositiveInfinity;
    private bool _wasOnFloor = true;
    private bool _wasSwimming;
    private CharacterProp _prop;
    private byte _gestureCount;
    private string? _gesture;
    private float _gestureTime;
    private float _bodyTurn;
    private bool _backward;

    /// <summary>
    /// Makes an animator for clips of the given lengths (seconds, by name). Missing clips are skipped. A
    /// <paramref name="firstPerson"/> animator (the local player's own arms) plays a clip's first-person version
    /// (<see cref="CharacterRig.FirstPersonSuffix"/>) where the set has one.
    /// </summary>
    public CharacterAnimator(CharacterSettings settings, IReadOnlyDictionary<string, float> clipLengths, bool firstPerson = false)
    {
        _settings = settings;
        _lengths = clipLengths;
        FirstPerson = firstPerson;
        _ground = Blend("idle", "walk", "jog", "run");
        _crouch = Blend("crouch_idle", "crouch_walk");
        _swim = Blend("swim_idle", "swim");
        _modeWeights[(int)CharacterMode.Ground] = 1f;
    }

    /// <summary>True when this animator poses the local player's first-person arms.</summary>
    public bool FirstPerson { get; }

    /// <summary>The whole-body layer: weights add up to 1.</summary>
    public IReadOnlyList<ClipWeight> Body => _body;

    /// <summary>The upper-body layer: weights add up to <see cref="UpperWeight"/>.</summary>
    public IReadOnlyList<ClipWeight> Upper => _upper;

    /// <summary>How much the upper-body layer replaces the body layer on <see cref="CharacterRig.UpperBodyBones"/>, 0 to 1.</summary>
    public float UpperWeight { get; private set; }

    /// <summary>What happened this frame.</summary>
    public IReadOnlyList<CharacterEvent> Events => _events;

    /// <summary>The state the body is fading toward.</summary>
    public CharacterMode Mode { get; private set; }

    /// <summary>Radians the body turns away from the look direction toward a sideways or backward move (positive is left).</summary>
    public float BodyTurn => _bodyTurn;

    /// <summary>Radians the neck and head pitch to follow the look.</summary>
    public float HeadPitch { get; private set; }

    /// <summary>Whether the prop in hand is drawn (not while swimming or seated).</summary>
    public bool ShowProp { get; private set; }

    /// <summary>How far the body has faded into <paramref name="mode"/>, 0 to 1.</summary>
    public float Weight(CharacterMode mode) => _modeWeights[(int)mode];

    /// <summary>How far the model moves up (negative: down) from the feet, to sit on a seat rather than stand on it.</summary>
    public float Lift => _modeWeights[(int)CharacterMode.Sit] * _settings.SeatedOffset;

    /// <summary>The gesture playing, or null.</summary>
    public string? Gesture => _gesture;

    /// <summary>How far into its clip <see cref="Gesture"/> is, in the clip's own seconds (it may play faster or slower).</summary>
    public float GestureTime => _gesture is null ? 0f : _gestureTime * _lengths[_gesture] / GestureLength(_gesture);

    /// <summary>How much <see cref="Gesture"/> shows over the hold pose, 0 to 1 (it fades in and out).</summary>
    public float GestureWeight { get; private set; }

    /// <summary>
    /// Starts <paramref name="gesture"/> now, outside the replicated gesture count: for things every peer sees
    /// for itself, such as flinching when a player's health drops.
    /// </summary>
    public void Play(CharacterGesture gesture)
    {
        if (Version(CharacterRig.GestureClip(CharacterRig.Sanitize(gesture))) is { } clip && _lengths.ContainsKey(clip))
        {
            _gesture = clip;
            _gestureTime = 0f;
        }
    }

    // The first-person version of a clip when this animator has one to play, else the clip itself.
    private string? Version(string? clip) =>
        FirstPerson && clip is not null && _lengths.ContainsKey(clip + CharacterRig.FirstPersonSuffix) ? clip + CharacterRig.FirstPersonSuffix : clip;

    /// <summary>Advances by <paramref name="delta"/> seconds with this frame's <paramref name="pose"/>.</summary>
    public void Update(CharacterPose pose, float delta)
    {
        _events.Clear();
        _body.Clear();
        _upper.Clear();
        if (!float.IsFinite(delta) || delta < 0f)
            delta = 0f;
        var velocity = Finite(pose.Velocity);
        var horizontal = new Vector2(velocity.X, velocity.Z).Length();
        _clock += delta;

        if (!_started)
        {
            // The first frame sets the scene without sounds: a player spawning in water or holding a tool.
            _started = true;
            _wasOnFloor = pose.OnFloor;
            _wasSwimming = pose.Swimming;
            _prop = CharacterRig.Sanitize(pose.Prop);
            _gestureCount = pose.GestureCount;
            Mode = Choose(pose);
            Array.Clear(_modeWeights);
            _modeWeights[(int)Mode] = 1f;
            if (Version(CharacterRig.HoldClip(_prop, pose.Guard)) is { } hold)
                _holds[hold] = 1f;
        }

        Track(pose, velocity, delta);
        Mode = Choose(pose);
        for (var i = 0; i < _modeWeights.Length; i++)
            _modeWeights[i] = Toward(_modeWeights[i], i == (int)Mode ? 1f : 0f, delta / _settings.ModeFade);
        Normalize(_modeWeights);

        Turn(pose, velocity, horizontal, delta);
        HeadPitch = float.IsFinite(pose.Pitch) ? pose.Pitch * _settings.HeadPitchShare : 0f;
        BuildBody(pose, horizontal, delta);
        BuildUpper(pose, delta);
        ShowProp = _prop != CharacterProp.None && _modeWeights[(int)CharacterMode.Swim] < 0.5f && _modeWeights[(int)CharacterMode.Sit] < 0.5f;
    }

    // Jumps, landings, water and the item in hand.
    private void Track(CharacterPose pose, Vector3 velocity, float delta)
    {
        if (_wasOnFloor && !pose.OnFloor && !pose.Swimming && !pose.Seated && velocity.Y > _settings.JumpDetectSpeed)
        {
            _jumped = true;
            _airTime = 0f;
            _events.Add(CharacterEvent.Jump);
        }
        if (!pose.OnFloor && !pose.Swimming && !pose.Seated)
        {
            _airTime += delta;
            _peakFall = MathF.Max(_peakFall, -velocity.Y);
        }
        else
        {
            if (!_wasOnFloor && pose.OnFloor && !pose.Swimming && !pose.Seated && (_airTime > _settings.AirGrace || _jumped)
                && _peakFall >= _settings.LandMinSpeed)
            {
                _events.Add(_peakFall >= _settings.HardLandSpeed ? CharacterEvent.LandHard : CharacterEvent.Land);
                _landTime = 0f;
            }
            _airTime = 0f;
            _peakFall = 0f;
            _jumped = false;
        }
        _landTime += delta;

        if (pose.Swimming != _wasSwimming)
            _events.Add(pose.Swimming ? CharacterEvent.WaterEnter : CharacterEvent.WaterExit);

        var prop = CharacterRig.Sanitize(pose.Prop);
        if (prop != _prop)
        {
            // Filling or emptying the bucket changes the prop, but nothing was taken in hand.
            if (prop != CharacterProp.None && !(IsBucket(prop) && IsBucket(_prop)))
                _events.Add(CharacterEvent.Equip);
            _prop = prop;
        }

        if (pose.GestureCount != _gestureCount)
        {
            _gestureCount = pose.GestureCount;
            if (Version(CharacterRig.GestureClip(CharacterRig.Sanitize(pose.Gesture), pose.GestureCount)) is { } clip && _lengths.ContainsKey(clip))
            {
                _gesture = clip;
                _gestureTime = 0f;
            }
        }
        else if (_gesture is not null)
        {
            _gestureTime += delta;
            if (_gestureTime >= GestureLength(_gesture))
                _gesture = null;
        }

        _wasOnFloor = pose.OnFloor;
        _wasSwimming = pose.Swimming;
    }

    private CharacterMode Choose(CharacterPose pose) =>
        pose.Seated ? CharacterMode.Sit
        : pose.Swimming ? CharacterMode.Swim
        : !pose.OnFloor && (_jumped || _airTime > _settings.AirGrace) ? CharacterMode.Air
        : pose.Crawl ? CharacterMode.Crouch
        : CharacterMode.Ground;

    // Turns the body toward the way it moves, up to the limit; moving backward plays the cycle in reverse.
    private void Turn(CharacterPose pose, Vector3 velocity, float horizontal, float delta)
    {
        var target = 0f;
        if (Mode is CharacterMode.Ground or CharacterMode.Crouch or CharacterMode.Swim && horizontal > _settings.MinStepSpeed)
        {
            var (sin, cos) = MathF.SinCos(float.IsFinite(pose.Yaw) ? pose.Yaw : 0f);
            var forward = -sin * velocity.X - cos * velocity.Z;
            var right = cos * velocity.X - sin * velocity.Z;
            var angle = MathF.Atan2(right, forward);
            _backward = MathF.Abs(angle) > MathF.PI * 0.55f;
            if (_backward)
                angle = angle > 0 ? angle - MathF.PI : angle + MathF.PI;
            var limit = _settings.MaxBodyTurnDegrees * MathF.PI / 180f;
            target = -Math.Clamp(angle, -limit, limit);
        }
        else if (Mode is not CharacterMode.Air)
        {
            _backward = false;
        }
        var step = _settings.BodyTurnRate * MathF.PI / 180f * delta;
        _bodyTurn = Toward(_bodyTurn, target, step);
    }

    private void BuildBody(CharacterPose pose, float horizontal, float delta)
    {
        var ground = _modeWeights[(int)CharacterMode.Ground];
        var crouch = _modeWeights[(int)CharacterMode.Crouch];
        var air = _modeWeights[(int)CharacterMode.Air];
        var swim = _modeWeights[(int)CharacterMode.Swim];
        var sit = _modeWeights[(int)CharacterMode.Sit];

        // One beat for walking and crouching, so a crossfade between them keeps the feet in step.
        var steps = Mode is CharacterMode.Ground or CharacterMode.Crouch;
        var set = Mode == CharacterMode.Crouch ? _crouch : _ground;
        var before = _phase;
        _phase = Advance(_phase, set, horizontal, delta);
        if (steps && pose.OnFloor && horizontal >= _settings.MinStepSpeed)
            Beat(before, _phase, _settings.StepPhases, i => i % 2 == 0 ? CharacterEvent.StepLeft : CharacterEvent.StepRight);

        var swimBefore = _swimPhase;
        _swimPhase = Advance(_swimPhase, _swim, horizontal, delta, minRate: 0.5f);
        if (Mode == CharacterMode.Swim && horizontal >= _settings.MinStepSpeed)
            Beat(swimBefore, _swimPhase, _settings.StrokePhases, _ => CharacterEvent.SwimStroke);

        if (ground > 0f)
        {
            var land = _lengths.TryGetValue("land", out var landLength) && _landTime < landLength
                ? (1f - _landTime / landLength) * (horizontal < 2f ? 1f : 0.4f)
                : 0f;
            AddBlend(_ground, horizontal, _phase, ground * (1f - land));
            Add("land", _landTime, ground * land);
        }
        if (crouch > 0f)
            AddBlend(_crouch, horizontal, _phase, crouch);
        if (air > 0f)
        {
            var jump = _jumped && _lengths.TryGetValue("jump", out var jumpLength) ? Math.Clamp((jumpLength - _airTime) / 0.1f, 0f, 1f) : 0f;
            Add("jump", _airTime, air * jump);
            Add("fall", _clock, air * (1f - jump));
        }
        if (swim > 0f)
            AddBlend(_swim, horizontal, _swimPhase, swim);
        if (sit > 0f)
            Add("sit", _clock, sit);
        if (_body.Count == 0)
            Add("idle", _clock, 1f);
    }

    private void BuildUpper(CharacterPose pose, float delta)
    {
        // Hands are busy swimming; a seated player holds the controls.
        var free = Mode is not (CharacterMode.Swim or CharacterMode.Sit);
        var want = free ? Version(CharacterRig.HoldClip(_prop, pose.Guard)) : null;
        if (want is not null && _lengths.ContainsKey(want))
            _holds.TryAdd(want, 0f);
        foreach (var clip in _holds.Keys.ToList())
        {
            var weight = Toward(_holds[clip], clip == want ? 1f : 0f, delta / _settings.HoldFade);
            if (weight <= 0f)
                _holds.Remove(clip);
            else
                _holds[clip] = weight;
        }
        var held = MathF.Min(1f, _holds.Values.Sum());

        var gesture = 0f;
        if (_gesture is not null)
        {
            var length = GestureLength(_gesture);
            gesture = Math.Clamp(MathF.Min(_gestureTime / _settings.GestureFadeIn, (length - _gestureTime) / _settings.GestureFadeOut), 0f, 1f);
        }

        var scale = held > 0f ? (1f - gesture) / MathF.Max(1f, _holds.Values.Sum()) : 0f;
        foreach (var (clip, weight) in _holds)
            AddTo(_upper, clip, _clock, weight * scale);
        if (_gesture is not null)
            AddTo(_upper, _gesture, _gestureTime * _lengths[_gesture] / GestureLength(_gesture), gesture);
        UpperWeight = Math.Clamp(held * (1f - gesture) + gesture, 0f, 1f);
        GestureWeight = gesture;
    }

    // Moves a cycle's phase on by distance: at a clip's own speed it plays at its own pace.
    private float Advance(float phase, (string Clip, float Speed)[] set, float speed, float delta, float minRate = 0f)
    {
        var rate = 0f;
        var total = 0f;
        foreach (var (clip, weight) in Weights(set, speed))
        {
            var clipSpeed = SpeedOf(clip);
            if (clipSpeed <= 0f || !_lengths.TryGetValue(clip, out var length) || length <= 0f)
                continue;
            rate += weight * MathF.Max(speed / clipSpeed, minRate) / length;
            total += weight;
        }
        if (total <= 0f && set.FirstOrDefault(s => s.Speed > 0f) is { Clip: { } moving } && _lengths.TryGetValue(moving, out var idleLength) && idleLength > 0f)
        {
            rate = minRate / idleLength;
            total = 1f;
        }
        if (total <= 0f)
            return phase;
        var next = phase + rate / total * delta * (_backward ? -1f : 1f);
        return next - MathF.Floor(next);
    }

    // Raises an event for each beat the phase passed between before and after (either direction).
    private void Beat(float before, float after, float[] beats, Func<int, CharacterEvent> kind)
    {
        if (before == after)
            return;
        var forward = !_backward;
        for (var i = 0; i < beats.Length; i++)
        {
            var b = beats[i];
            var crossed = forward
                ? (after >= before ? b > before && b <= after : b > before || b <= after)
                : (after <= before ? b < before && b >= after : b < before || b >= after);
            if (crossed)
                _events.Add(kind(i));
        }
    }

    private void AddBlend((string Clip, float Speed)[] set, float speed, float phase, float layerWeight)
    {
        foreach (var (clip, weight) in Weights(set, speed))
        {
            Add(clip, SpeedOf(clip) > 0f ? phase * _lengths[clip] : _clock, layerWeight * weight);
        }
    }

    // Linear blend between the two clips whose speeds bracket the current one.
    private IEnumerable<(string Clip, float Weight)> Weights((string Clip, float Speed)[] set, float speed)
    {
        if (set.Length == 0)
            yield break;
        if (set.Length == 1 || speed <= set[0].Speed)
        {
            yield return (set[0].Clip, 1f);
            yield break;
        }
        for (var i = 1; i < set.Length; i++)
        {
            if (speed > set[i].Speed && i < set.Length - 1)
                continue;
            var (lo, hi) = (set[i - 1], set[i]);
            var t = Math.Clamp((speed - lo.Speed) / MathF.Max(hi.Speed - lo.Speed, 1e-3f), 0f, 1f);
            if (t < 1f)
                yield return (lo.Clip, 1f - t);
            if (t > 0f)
                yield return (hi.Clip, t);
            yield break;
        }
    }

    // The clips of a blend that exist, sorted by the speed they are made for.
    private (string Clip, float Speed)[] Blend(params string[] clips) =>
        [.. clips.Where(_lengths.ContainsKey).Select(c => (c, SpeedOf(c))).OrderBy(c => c.Item2)];

    private float SpeedOf(string clip) => _settings.ClipSpeeds.GetValueOrDefault(clip);

    // How long a gesture plays: its own length unless the settings give it another (a first-person version or a
    // variant lasts as long as the clip it stands in for).
    private static bool IsBucket(CharacterProp prop) => prop is CharacterProp.WaterBucket or CharacterProp.WaterBucketFull;

    private float GestureLength(string clip)
    {
        var name = CharacterRig.GestureOf(clip.EndsWith(CharacterRig.FirstPersonSuffix, StringComparison.Ordinal) ? clip[..^CharacterRig.FirstPersonSuffix.Length] : clip);
        return _settings.GestureSeconds.TryGetValue(name, out var seconds) ? seconds : _lengths[clip];
    }

    private void Add(string clip, float time, float weight) => AddTo(_body, clip, time, weight);

    private void AddTo(List<ClipWeight> layer, string clip, float time, float weight)
    {
        if (weight <= 1e-4f || !_lengths.TryGetValue(clip, out var length) || length <= 0f)
            return;
        // One-shots hold their last frame; everything else loops.
        var t = IsOneShot(clip) ? Math.Clamp(time, 0f, length) : time - MathF.Floor(time / length) * length;
        layer.Add(new ClipWeight(clip, t, weight));
    }

    /// <summary>True for clips that play once (jump, land and gestures) rather than loop.</summary>
    public static bool IsOneShot(string clip) =>
        clip is "jump" or "land" || (CharacterRig.UpperBodyClips.Contains(clip) && !clip.StartsWith("hold_", StringComparison.Ordinal));

    private static void Normalize(float[] weights)
    {
        var sum = weights.Sum();
        if (sum <= 0f)
            return;
        for (var i = 0; i < weights.Length; i++)
            weights[i] /= sum;
    }

    private static float Toward(float from, float to, float maxStep) =>
        MathF.Abs(to - from) <= maxStep ? to : from + MathF.Sign(to - from) * maxStep;

    private static Vector3 Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) ? v : Vector3.Zero;
}
