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
/// <param name="Guard">The tool in hand raised (<see cref="CharacterRig.CanGuard"/>).</param>
/// <param name="Seat">How the seat holds a seated character (null: <see cref="SeatStyle.Default"/>). <see cref="Velocity"/> is
/// then the seat's own, which bounces the body.</param>
/// <param name="Dead">Dead, until the respawn: the body collapses (<c>death</c>) and lies there (<c>downed</c>).</param>
/// <param name="Climbing">On a ladder: the climb clip plays, its pace set by <see cref="Velocity"/>'s vertical part, and the hands
/// leave what they hold for the rungs.</param>
public readonly record struct CharacterPose(
    Vector3 Velocity, float Yaw, float Pitch, bool OnFloor, bool Swimming = false, bool Crawl = false, bool Seated = false,
    CharacterProp Prop = CharacterProp.None, CharacterGesture Gesture = CharacterGesture.None, byte GestureCount = 0,
    bool Guard = false, SeatStyle? Seat = null, bool Dead = false, bool Climbing = false);

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

    /// <summary>On a ladder.</summary>
    Climb,
}

/// <summary>One clip's contribution to a pose.</summary>
/// <param name="Clip">Clip name (<see cref="CharacterRig.Clips"/>).</param>
/// <param name="Time">Where in the clip, in seconds.</param>
/// <param name="Weight">Its share of the layer, 0 to 1.</param>
public readonly record struct ClipWeight(string Clip, float Time, float Weight);

/// <summary>
/// The animation state machine every player character shares. Each frame it takes a <see cref="CharacterPose"/>
/// and says which clips to blend, where and how much: a whole-body layer (moving, crouching, jumping,
/// swimming, sitting), an upper-body layer (holding a tool, gestures, hit reactions) on <see cref="CharacterRig.UpperBodyBones"/>
/// and a legs layer for the gestures that step into a blow or fall down (<see cref="CharacterRig.LegGestures"/>).
/// Walking and running clips are driven by distance travelled rather than time, so the feet keep step with
/// the ground at any speed and every moving clip strikes on the same beat; footsteps come from that beat.
/// Beyond the clips it says how far the upper body turns from the feet (<see cref="Twist"/>: standing, the feet stay
/// put while the look turns, and step round with <c>turn_l</c> or <c>turn_r</c> once it has turned far enough) and
/// how the body leans into speeding up and turning (<see cref="Lean"/>).
/// It runs on every peer from replicated state, so it needs no network traffic of its own (a hit's reaction comes
/// with the hit, <see cref="React"/>).
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
    private readonly List<ClipWeight> _legs = [];
    private readonly List<CharacterEvent> _events = [];
    private readonly (string Clip, float Speed)[] _ground;
    private readonly (string Clip, float Speed)[] _crouch;
    private readonly (string Clip, float Speed)[] _swim;
    private bool _started;
    private float _clock;
    private float _phase;
    private float _swimPhase;
    // Where in the climb cycle the body is, 0 to 1: it moves on with the height climbed, so hands and feet keep to the rungs.
    private float _climbPhase;
    private float _airTime;
    private bool _jumped;
    private float _peakFall;
    private float _landTime = float.PositiveInfinity;
    private bool _wasOnFloor = true;
    private bool _wasSwimming;
    private CharacterProp _prop;
    private CharacterProp _stowing;
    private byte _gestureCount;
    private string? _gesture;
    private float _gestureTime;
    private float _bodyTurn;
    private bool _backward;
    private bool _riding;
    private Vector3 _seatVelocity;
    private float _bob, _bobSpeed;
    private Vector2 _lean, _leanSpeed;
    private float _seatFit;
    private float _blendSpeed;
    private Vector2 _planar;
    private bool _havePlanar;
    private Vector2 _tilt, _tiltRate;
    private float _yaw;
    private bool _haveYaw;
    private string? _turn;
    private float _turnTime, _turnFrom;
    private float _stillTime, _nextFidget;
    private int _fidgetIndex;
    private string? _fidget;
    private float _fidgetTime;
    private string _jumpClip = "jump";
    private string _landClip = "land";
    private string? _chain;
    private bool _chained;
    private bool _wasDead;
    private bool _seated;

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
        _nextFidget = _settings.Motion.FidgetAfter[0];
    }

    /// <summary>Metres climbed in one cycle of the <c>climb</c> clip: each hand and foot moves up two rungs of a ladder (0.25 m apart).</summary>
    public const float ClimbCycleMetres = 0.5f;

    /// <summary>True when this animator poses the local player's first-person arms.</summary>
    public bool FirstPerson { get; }

    /// <summary>The whole-body layer: weights add up to 1.</summary>
    public IReadOnlyList<ClipWeight> Body => _body;

    /// <summary>The upper-body layer: weights add up to <see cref="UpperWeight"/>.</summary>
    public IReadOnlyList<ClipWeight> Upper => _upper;

    /// <summary>How much the upper-body layer replaces the body layer on <see cref="CharacterRig.UpperBodyBones"/>, 0 to 1.</summary>
    public float UpperWeight { get; private set; }

    /// <summary>The legs layer: a gesture's or reaction's own hips and legs (weights add up to 1, or nothing).</summary>
    public IReadOnlyList<ClipWeight> Legs => _legs;

    /// <summary>How much the legs layer replaces the body layer on the bones outside <see cref="CharacterRig.UpperBodyBones"/>, 0 to 1.</summary>
    public float LegsWeight { get; private set; }

    /// <summary>
    /// How much of the body layer's spine motion (relative to its rest) is added back over a hold while moving, 0 to 1
    /// (<see cref="MotionSettings.HoldCarry"/>): the stride still sways a torso that holds something.
    /// </summary>
    public float Carry { get; private set; }

    /// <summary>The share of the holds that leave the left arm hanging free (<see cref="CharacterRig.FreeArmHolds"/>), 0 to 1: it swings with the stride by <see cref="Carry"/>.</summary>
    public float FreeArm { get; private set; }

    /// <summary>
    /// Radians the upper body turns from the hips and feet, positive to its left (shared out with
    /// <see cref="MotionSettings.TwistShares"/>), so the chest and head face where the player looks while the feet point
    /// elsewhere.
    /// </summary>
    public float Twist { get; private set; }

    /// <summary>
    /// How far the body leans with its acceleration, in radians: X forward (negative: back), Y to its left (negative: right).
    /// </summary>
    public Vector2 Lean { get; private set; }

    /// <summary>True while knocked down, getting up or dead: the player can't move (<see cref="CharacterRig.GroundingClips"/>).</summary>
    public bool Grounded => _gesture is { } g && CharacterRig.GroundingClips.Contains(GestureName(g));

    /// <summary>The whole-body fidget playing in place of idle, or null.</summary>
    public string? Fidget => _fidget;

    /// <summary>The turn on the spot playing (<c>turn_l</c>, <c>turn_r</c>), or null.</summary>
    public string? TurnClip => _turn;

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

    /// <summary>
    /// The prop to draw: the one in hand, or one still being put away (<see cref="CharacterRig.StowClip"/>) until its
    /// clip ends.
    /// </summary>
    public CharacterProp Prop =>
        _stowing != CharacterProp.None && _gesture is { } gesture && gesture == Version(CharacterRig.StowClip(_stowing)) ? _stowing : _prop;

    /// <summary>How far the body has faded into <paramref name="mode"/>, 0 to 1.</summary>
    public float Weight(CharacterMode mode) => _modeWeights[(int)mode];

    /// <summary>
    /// How far the model moves up (negative: down) from the feet, to sit on a seat rather than stand on it: the hips land on
    /// the cushion (where the sitting clips can't reach it) and bob with the ride.
    /// </summary>
    public float Lift => _modeWeights[(int)CharacterMode.Sit] * (_settings.SeatedOffset + _seatFit + _bob);

    /// <summary>
    /// How far a seated body's upper half sways with the ride, in radians in the body's frame: X tips it back (positive)
    /// or forward, Y leans it to its left (positive) or right.
    /// </summary>
    public Vector2 SeatLean => _modeWeights[(int)CharacterMode.Sit] * _lean;

    /// <summary>How far a seated body's hips bob from the cushion with the ride, in m (negative is down).</summary>
    public float SeatBob => _bob;

    /// <summary>The gesture playing, or null.</summary>
    public string? Gesture => _gesture;

    /// <summary>How far into its clip <see cref="Gesture"/> is, in the clip's own seconds (it may play faster or slower).</summary>
    public float GestureTime => _gesture is null ? 0f : ClipTime(_gesture, _gestureTime);

    /// <summary>How much <see cref="Gesture"/> shows over the hold pose, 0 to 1 (it fades in and out).</summary>
    public float GestureWeight { get; private set; }

    /// <summary>
    /// Starts <paramref name="gesture"/> now, outside the replicated gesture count: for things every peer sees
    /// for itself, such as flinching when a player's health drops. A hit reaction already playing is not cut short.
    /// </summary>
    public void Play(CharacterGesture gesture)
    {
        if (_gesture is { } playing && CharacterRig.ReactionClips.Contains(playing))
            return;
        if (Version(CharacterRig.GestureClip(CharacterRig.Sanitize(gesture))) is { } clip && _lengths.ContainsKey(clip) && !Grounded)
            Start(clip, null);
    }

    /// <summary>
    /// Reacts to a hit now, on every peer that sees it (the hit says how hard and where from): a light hit flinches away
    /// from <paramref name="from"/> (radians from where the player looks, positive to its left; null for nowhere in
    /// particular), a heavy one stumbles back, a knockdown throws the player down and then up again. A player already down
    /// or dead doesn't react; a seated one only flinches. Without the reaction clips it plays <c>hurt</c>.
    /// </summary>
    public void React(HitReaction reaction, float? from = null)
    {
        if (reaction == HitReaction.None || Grounded || _wasDead)
            return;
        if (_seated)
            reaction = HitReaction.Light;
        // The angle is from the look; the body may be turned from it.
        var clip = CharacterRig.HitClip(reaction, from is { } a && float.IsFinite(a) ? a - _bodyTurn : null);
        if (Version(clip) is not { } version || !_lengths.ContainsKey(version))
        {
            Play(CharacterGesture.Hurt);
            return;
        }
        Start(version, clip == "knockdown" ? "get_up" : null);
    }

    /// <summary>
    /// Gets up from lying on the back (<c>get_up</c>), as a player coming to after the opening cinematic: grounded until
    /// it ends. False when there is no such clip, or the player is down, dead or seated already.
    /// </summary>
    public bool GetUp()
    {
        if (Grounded || _wasDead || _seated || Version("get_up") is not { } clip || !_lengths.ContainsKey(clip))
            return false;
        Start(clip, null);
        return true;
    }

    // Starts a gesture or reaction clip; chain (if any) follows it straight on, after lie_seconds for a knockdown.
    private void Start(string clip, string? chain)
    {
        _gesture = clip;
        _gestureTime = 0f;
        _chain = chain;
        _chained = false;
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
        _legs.Clear();
        if (!float.IsFinite(delta) || delta < 0f)
            delta = 0f;
        var velocity = Finite(pose.Velocity);
        var horizontal = new Vector2(velocity.X, velocity.Z).Length();
        _clock += delta;
        _seated = pose.Seated;
        _blendSpeed += (horizontal - _blendSpeed) * (1f - MathF.Exp(-delta / _settings.Motion.BlendSeconds));

        if (!_started)
        {
            // The first frame sets the scene without sounds: a player spawning in water or holding a tool.
            _started = true;
            _wasOnFloor = pose.OnFloor;
            _wasSwimming = pose.Swimming;
            _prop = CharacterRig.Sanitize(pose.Prop);
            _gestureCount = pose.GestureCount;
            _blendSpeed = horizontal;
            _wasDead = pose.Dead;
            // Already dead when first seen (joining a world where they lie): lying there.
            if (pose.Dead && Version("downed") is { } downed && _lengths.ContainsKey(downed))
                Start(downed, null);
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

        Ride(pose, velocity, delta);
        Turn(pose, velocity, horizontal, delta);
        Tilt(pose, velocity, delta);
        Fidgets(pose, horizontal, delta);
        HeadPitch = float.IsFinite(pose.Pitch) ? pose.Pitch * _settings.HeadPitchShare : 0f;
        BuildBody(pose, horizontal, delta);
        BuildUpper(pose, delta);
        // Lying on the ground, the twist and the lean give way to the clip.
        var down = Grounded ? GestureWeight : 0f;
        Twist *= 1f - down;
        Lean *= 1f - MathF.Max(down, LegsWeight);
        ShowProp = Prop != CharacterProp.None && _modeWeights[(int)CharacterMode.Swim] < 0.5f && _modeWeights[(int)CharacterMode.Sit] < 0.5f
            && _modeWeights[(int)CharacterMode.Climb] < 0.5f && !Grounded;
    }

    // Jumps, landings, water and the item in hand.
    private void Track(CharacterPose pose, Vector3 velocity, float delta)
    {
        if (_wasOnFloor && !pose.OnFloor && !pose.Swimming && !pose.Seated && !pose.Climbing && velocity.Y > _settings.JumpDetectSpeed)
        {
            _jumped = true;
            _airTime = 0f;
            _events.Add(CharacterEvent.Jump);
            // Taking off at a run is a leap, one leg out in front.
            _jumpClip = new Vector2(velocity.X, velocity.Z).Length() >= _settings.Motion.RunJumpSpeed && _lengths.ContainsKey("jump_run") ? "jump_run" : "jump";
        }
        if (!pose.OnFloor && !pose.Swimming && !pose.Seated && !pose.Climbing)
        {
            _airTime += delta;
            _peakFall = MathF.Max(_peakFall, -velocity.Y);
        }
        else
        {
            if (!_wasOnFloor && pose.OnFloor && !pose.Swimming && !pose.Seated && (_airTime > _settings.AirGrace || _jumped)
                && _peakFall >= _settings.LandMinSpeed)
            {
                var hard = _peakFall >= _settings.HardLandSpeed;
                _events.Add(hard ? CharacterEvent.LandHard : CharacterEvent.Land);
                _landClip = hard && _lengths.ContainsKey("land_hard") ? "land_hard" : "land";
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
            var old = _prop;
            _prop = prop;
            _stowing = CharacterProp.None;
            // Filling or emptying the bucket changes the prop, but nothing was taken in hand.
            if (prop != CharacterProp.None && !(IsBucket(prop) && IsBucket(old)))
                _events.Add(CharacterEvent.Equip);
            // A prop with a draw or stow clip (the Refine Tool) plays it as a gesture; the new prop's draw comes first.
            if (!Grounded && Version(CharacterRig.DrawClip(prop)) is { } draw && _lengths.ContainsKey(draw))
            {
                Start(draw, null);
            }
            else if (!Grounded && Version(CharacterRig.StowClip(old)) is { } stow && _lengths.ContainsKey(stow))
            {
                Start(stow, null);
                _stowing = old;
            }
        }

        // Dying drops the body (death, then downed until the respawn); coming back to life stands it up again.
        if (pose.Dead && !_wasDead && Version("death") is { } death && _lengths.ContainsKey(death))
            Start(death, "downed");
        else if (!pose.Dead && _wasDead && Grounded)
            _gesture = _chain = null;
        _wasDead = pose.Dead;

        if (pose.GestureCount != _gestureCount)
        {
            _gestureCount = pose.GestureCount;
            if (Version(CharacterRig.GestureClip(CharacterRig.Sanitize(pose.Gesture), pose.GestureCount)) is { } clip && _lengths.ContainsKey(clip) && !Grounded)
                Start(clip, null);
        }
        else if (_gesture is not null)
        {
            _gestureTime += delta;
            var length = GestureLength(_gesture);
            if (_chain is not null)
            {
                // A knockdown lies a moment before getting up; a death goes straight into lying still.
                var lie = GestureName(_gesture) == "knockdown" ? _settings.Motion.LieSeconds : 0f;
                if (_gestureTime >= length + lie)
                {
                    var next = Version(_chain);
                    _chain = null;
                    if (next is not null && _lengths.ContainsKey(next))
                    {
                        Start(next, null);
                        _chained = true;
                    }
                    else
                    {
                        _gesture = null;
                    }
                }
            }
            else if (_gestureTime >= length && !(pose.Dead && GestureName(_gesture) == "downed"))
            {
                _gesture = null;
            }
        }


        _wasOnFloor = pose.OnFloor;
        _wasSwimming = pose.Swimming;
    }

    private CharacterMode Choose(CharacterPose pose) =>
        pose.Seated ? CharacterMode.Sit
        : pose.Climbing ? CharacterMode.Climb
        : pose.Swimming ? CharacterMode.Swim
        : !pose.OnFloor && (_jumped || _airTime > _settings.AirGrace) ? CharacterMode.Air
        : pose.Crawl ? CharacterMode.Crouch
        : CharacterMode.Ground;

    // Turns the body toward the way it moves, up to the limit; moving backward plays the cycle in reverse. Standing, the
    // feet stay where they point while the look turns, until it has turned far enough that they step round after it.
    private void Turn(CharacterPose pose, Vector3 velocity, float horizontal, float delta)
    {
        var motion = _settings.Motion;
        var yaw = float.IsFinite(pose.Yaw) ? pose.Yaw : 0f;
        var turned = _haveYaw ? MathF.IEEERemainder(yaw - _yaw, MathF.Tau) : 0f;
        _yaw = yaw;
        _haveYaw = true;
        var standing = Mode is CharacterMode.Ground or CharacterMode.Crouch && pose.OnFloor && !pose.Seated && !Grounded
            && horizontal <= _settings.MinStepSpeed && !(LegsWeight > 0.5f);
        if (standing)
        {
            _backward = false;
            _bodyTurn -= turned;
            var limit = motion.MaxTwistDegrees * MathF.PI / 180f;
            if (_turn is { } turn)
            {
                // The turn's own curve carries the body round while its feet step; a look still turning moves where it ends.
                _turnFrom -= turned;
                _turnTime += delta;
                var length = _lengths[turn];
                var u = Math.Clamp(_turnTime / length, 0f, 1f);
                _bodyTurn = _turnFrom * (1f - u * u * (3f - 2f * u));
                if (_turnTime >= length)
                    _turn = null;
            }
            else if (MathF.Abs(_bodyTurn) > motion.TurnInPlaceDegrees * MathF.PI / 180f
                && (_bodyTurn < 0f ? "turn_l" : "turn_r") is var clip && _lengths.ContainsKey(clip))
            {
                // The look went left of the feet (the body is turned right of it): step round to the left.
                _turn = clip;
                _turnTime = 0f;
                _turnFrom = _bodyTurn;
            }
            _bodyTurn = Math.Clamp(_bodyTurn, -limit, limit);
            _turnFrom = Math.Clamp(_turnFrom, -limit, limit);
            Twist = -_bodyTurn * TwistShare();
            return;
        }
        _turn = null;
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
        // Turning the look turns the body with it; the feet then catch up with where they should point.
        _bodyTurn = Toward(_bodyTurn, target, step);
        Twist = pose.Seated || Mode is CharacterMode.Swim or CharacterMode.Climb ? 0f : -_bodyTurn * TwistShare();
    }

    // All of the turn standing, less of it as the body gets moving (a runner's shoulders follow the hips a little).
    private float TwistShare() =>
        1f - (1f - _settings.Motion.MovingTwistShare) * Math.Clamp(_blendSpeed / 2f, 0f, 1f);

    // Leans into the acceleration: forward speeding up, back slowing down, into a turn; on a damped spring, so it
    // overshoots a little and settles, as a body's weight does.
    private void Tilt(CharacterPose pose, Vector3 velocity, float delta)
    {
        var motion = _settings.Motion;
        var planar = new Vector2(velocity.X, velocity.Z);
        var accel = _havePlanar && delta > 0f ? (planar - _planar) / delta : Vector2.Zero;
        _planar = planar;
        _havePlanar = true;
        var target = Vector2.Zero;
        var grounded = Mode is CharacterMode.Ground or CharacterMode.Crouch && pose.OnFloor && !pose.Seated;
        // A teleport or a respawn is not an acceleration.
        if (grounded && accel.Length() < 200f)
        {
            var (sin, cos) = MathF.SinCos(_yaw + _bodyTurn);
            var forward = -sin * accel.X - cos * accel.Y;
            var right = cos * accel.X - sin * accel.Y;
            var max = motion.MaxLeanDegrees * MathF.PI / 180f;
            target = new Vector2(forward, -right) * (motion.LeanPerAcceleration * MathF.PI / 180f);
            if (target.Length() > max)
                target = Vector2.Normalize(target) * max;
            // Running, the body leans into it, more the faster.
            var (from, full) = (motion.RunLeanSpeeds[0], motion.RunLeanSpeeds[1]);
            target.X += motion.RunLeanDegrees * MathF.PI / 180f * Math.Clamp((planar.Length() - from) / (full - from), 0f, 1f);
        }
        for (var left = delta; left > 0f; left -= 1f / 120f)
        {
            var h = MathF.Min(left, 1f / 120f);
            _tiltRate += ((target - _tilt) * motion.LeanStiffness - _tiltRate * motion.LeanDamping) * h;
            _tilt += _tiltRate * h;
        }
        if (!float.IsFinite(_tilt.X) || !float.IsFinite(_tilt.Y))
            _tilt = _tiltRate = Vector2.Zero;
        Lean = _tilt;
    }

    // After standing still a while, plays a fidget in place of idle now and then.
    private void Fidgets(CharacterPose pose, float horizontal, float delta)
    {
        var motion = _settings.Motion;
        var still = Mode == CharacterMode.Ground && pose.OnFloor && !pose.Seated && horizontal <= _settings.MinStepSpeed
            && _gesture is null && _turn is null;
        if (_fidget is { } fidget)
        {
            _fidgetTime += delta;
            if (_fidgetTime >= _lengths[fidget] || (!still && _blendSpeed > 1f))
                _fidget = null;
            return;
        }
        if (!still)
        {
            _stillTime = 0f;
            _nextFidget = motion.FidgetAfter[_fidgetIndex % motion.FidgetAfter.Length];
            return;
        }
        _stillTime += delta;
        if (_stillTime < _nextFidget || motion.Fidgets.Length == 0)
            return;
        var next = motion.Fidgets[_fidgetIndex % motion.Fidgets.Length];
        _fidgetIndex++;
        _nextFidget = _stillTime + motion.FidgetAfter[_fidgetIndex % motion.FidgetAfter.Length];
        if (_lengths.ContainsKey(next))
        {
            _fidget = next;
            _fidgetTime = 0f;
            _nextFidget += _lengths[next];
        }
    }


    private void BuildBody(CharacterPose pose, float horizontal, float delta)
    {
        // Each fade eases in and out rather than running at a constant rate.
        var eased = new float[_modeWeights.Length];
        for (var i = 0; i < eased.Length; i++)
            eased[i] = _modeWeights[i] * _modeWeights[i] * (3f - 2f * _modeWeights[i]);
        Normalize(eased);
        var ground = eased[(int)CharacterMode.Ground];
        var crouch = eased[(int)CharacterMode.Crouch];
        var air = eased[(int)CharacterMode.Air];
        var swim = eased[(int)CharacterMode.Swim];
        var sit = eased[(int)CharacterMode.Sit];
        var climb = eased[(int)CharacterMode.Climb];

        // One beat for walking and crouching, so a crossfade between them keeps the feet in step.
        var steps = Mode is CharacterMode.Ground or CharacterMode.Crouch;
        var set = Mode == CharacterMode.Crouch ? _crouch : _ground;
        var before = _phase;
        _phase = Advance(_phase, set, horizontal, _blendSpeed, delta);
        if (steps && pose.OnFloor && horizontal >= _settings.MinStepSpeed)
            Beat(before, _phase, _settings.StepPhases, i => i % 2 == 0 ? CharacterEvent.StepLeft : CharacterEvent.StepRight);

        var swimBefore = _swimPhase;
        _swimPhase = Advance(_swimPhase, _swim, horizontal, horizontal, delta, minRate: 0.5f);
        if (Mode == CharacterMode.Swim && horizontal >= _settings.MinStepSpeed)
            Beat(swimBefore, _swimPhase, _settings.StrokePhases, _ => CharacterEvent.SwimStroke);

        if (ground > 0f)
        {
            // The landing takes over and gives way along its length: all of it at first, none at its end.
            var land = _lengths.TryGetValue(_landClip, out var landLength) && _landTime < landLength
                ? (1f - Smooth(_landTime / landLength)) * (horizontal < 2f ? 1f : 0.4f)
                : 0f;
            // Standing, a turn on the spot or a fidget stands in for idle (fading in and out at its ends).
            var standIn = 0f;
            string? standClip = null;
            var standTime = 0f;
            if (_turn is { } turn)
            {
                (standClip, standTime) = (turn, _turnTime);
                standIn = Math.Clamp(MathF.Min(_turnTime, _lengths[turn] - _turnTime) / 0.08f, 0f, 1f);
            }
            else if (_fidget is { } fidget)
            {
                (standClip, standTime) = (fidget, _fidgetTime);
                standIn = Smooth(Math.Clamp(MathF.Min(_fidgetTime, _lengths[fidget] - _fidgetTime) / _settings.Motion.FidgetFade, 0f, 1f));
            }
            var idle = Weights(_ground, _blendSpeed).Where(w => w.Clip == "idle").Sum(w => w.Weight);
            standIn *= idle;
            AddBlend(_ground, _blendSpeed, _phase, ground * (1f - land), skipIdle: standIn);
            if (standClip is not null)
                Add(standClip, standTime, ground * (1f - land) * standIn);
            Add(_landClip, _landTime, ground * land);
        }
        if (crouch > 0f)
            AddBlend(_crouch, _blendSpeed, _phase, crouch);
        if (air > 0f)
        {
            var jump = _jumped && _lengths.TryGetValue(_jumpClip, out var jumpLength) ? Math.Clamp((jumpLength - _airTime) / 0.12f, 0f, 1f) : 0f;
            Add(_jumpClip, _airTime, air * jump);
            Add("fall", _clock, air * (1f - jump));
        }
        if (swim > 0f)
            AddBlend(_swim, horizontal, _swimPhase, swim);
        if (sit > 0f)
            AddSit(pose.Seat ?? SeatStyle.Default, sit);
        // Climbing plays the cycle forward going up and backward coming down; held still it holds where it is.
        if (pose.Climbing)
            _climbPhase = (_climbPhase + Finite(pose.Velocity).Y * delta / ClimbCycleMetres) % 1f;
        if (_climbPhase < 0f)
            _climbPhase += 1f;
        if (climb > 0f)
        {
            if (_lengths.TryGetValue("climb", out var climbLength))
                Add("climb", _climbPhase * climbLength, climb);
            else
                Add("fall", _clock, climb);
        }
        if (_body.Count == 0)
            Add("idle", _clock, 1f);
    }

    // The sitting clips for the seat: a chair (sit) blended with a low seat (sit_low) so the hips land on its cushion, with
    // the hands on a wheel or (sit_ride, sit_ride_low) on the thighs. A saddle is ridden astride (sit_saddle, hands on the
    // bars, or sit_saddle_ride, hands on the thighs). What the blend can't reach the model's lift makes up.
    private void AddSit(SeatStyle seat, float weight)
    {
        var hips = _settings.Seat.ClipHips;
        if (seat.Saddle && _lengths.ContainsKey("sit_saddle"))
        {
            var clip = seat.HandsFree && _lengths.ContainsKey("sit_saddle_ride") ? "sit_saddle_ride" : "sit_saddle";
            var saddleHips = hips.GetValueOrDefault("sit_saddle", SeatRideSettings.SaddleHips);
            _seatFit = float.IsFinite(seat.Cushion) ? seat.Cushion + _settings.Seat.HipsAboveCushion - saddleHips : 0f;
            Add(clip, _clock, weight);
            return;
        }
        var (high, low) = seat.HandsFree && _lengths.ContainsKey("sit_ride") ? ("sit_ride", "sit_ride_low") : ("sit", "sit_low");
        var highHips = hips.GetValueOrDefault("sit", 0.53f);
        var lowHips = hips.GetValueOrDefault("sit_low", highHips);
        var want = float.IsFinite(seat.Cushion) ? seat.Cushion + _settings.Seat.HipsAboveCushion : highHips;
        var toLow = _lengths.ContainsKey(low) && highHips > lowHips ? Math.Clamp((highHips - want) / (highHips - lowHips), 0f, 1f) : 0f;
        _seatFit = want - (highHips + (lowHips - highHips) * toLow);
        Add(high, _clock, weight * (1f - toLow));
        Add(low, _clock, weight * toLow);
    }

    // The ride: the hips bob and the upper body sways on damped springs, pushed by each change in the seat's velocity.
    // Sitting down starts the hips sinking into the seat, so the body settles.
    private void Ride(CharacterPose pose, Vector3 velocity, float delta)
    {
        var settings = _settings.Seat;
        if (!pose.Seated)
        {
            _riding = false;
            _bob = _bobSpeed = 0f;
            _lean = _leanSpeed = Vector2.Zero;
            return;
        }
        var bounce = Math.Clamp((pose.Seat ?? SeatStyle.Default).Bounce, 0f, Data.SeatDefinition.MaxBounce);
        if (!_riding)
        {
            _riding = true;
            _seatVelocity = velocity;
            _bobSpeed = -settings.SettleSpeed * MathF.Min(bounce, 2f);
        }
        var change = velocity - _seatVelocity;
        _seatVelocity = velocity;
        if (change.Length() <= settings.MaxImpulse)
        {
            var (sin, cos) = MathF.SinCos(float.IsFinite(pose.Yaw) ? pose.Yaw : 0f);
            var forward = -sin * change.X - cos * change.Z;
            var right = cos * change.X - sin * change.Z;
            // The seat jumps up, the hips lag below it; it speeds up, the body tips back; it swings right, the body leans left.
            _bobSpeed -= change.Y * settings.BobGain * bounce;
            _leanSpeed += new Vector2(forward, right) * settings.LeanGain * bounce;
        }
        // A bouncier seat also rings for longer.
        var soften = 1f / (0.5f + 0.5f * MathF.Max(bounce, 0.25f));
        var maxLean = settings.MaxLeanDegrees * MathF.PI / 180f;
        for (var left = delta; left > 0f; left -= 1f / 120f)
        {
            var h = MathF.Min(left, 1f / 120f);
            _bobSpeed += (-settings.BobStiffness * _bob - settings.BobDamping * soften * _bobSpeed) * h;
            _bob += _bobSpeed * h;
            _leanSpeed += (-settings.LeanStiffness * _lean - settings.LeanDamping * soften * _leanSpeed) * h;
            _lean += _leanSpeed * h;
        }
        if (MathF.Abs(_bob) > settings.MaxBob)
        {
            _bob = Math.Clamp(_bob, -settings.MaxBob, settings.MaxBob);
            _bobSpeed = 0f;
        }
        if (_lean.Length() > maxLean)
        {
            _lean = Vector2.Normalize(_lean) * maxLean;
            _leanSpeed = Vector2.Zero;
        }
    }

    private void BuildUpper(CharacterPose pose, float delta)
    {
        // Hands are busy swimming or on the rungs; a seated player holds the controls.
        var free = Mode is not (CharacterMode.Swim or CharacterMode.Sit or CharacterMode.Climb) && !Grounded;
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
            var reaction = CharacterRig.ReactionClips.Contains(_gesture);
            var fadeIn = _chained ? 1f : _gestureTime / (reaction ? _settings.Motion.ReactionFadeIn : _settings.GestureFadeIn);
            // A clip with another to follow, or lying dead, stays on to its end.
            var fadeOut = _chain is not null || (pose.Dead && GestureName(_gesture) == "downed")
                ? 1f
                : (length - _gestureTime) / (reaction ? _settings.Motion.ReactionFadeOut : _settings.GestureFadeOut);
            gesture = Math.Clamp(MathF.Min(fadeIn, fadeOut), 0f, 1f);
        }

        var scale = held > 0f ? (1f - gesture) / MathF.Max(1f, _holds.Values.Sum()) : 0f;
        foreach (var (clip, weight) in _holds)
            AddTo(_upper, clip, _clock, weight * scale);
        if (_gesture is not null)
            AddTo(_upper, _gesture, ClipTime(_gesture, _gestureTime), gesture);
        UpperWeight = Math.Clamp(held * (1f - gesture) + gesture, 0f, 1f);
        GestureWeight = gesture;
        var moving = (_modeWeights[(int)CharacterMode.Ground] + _modeWeights[(int)CharacterMode.Crouch])
            * Math.Clamp(_blendSpeed / _settings.Motion.CarryFullSpeed, 0f, 1f);
        Carry = Math.Clamp(held * (1f - gesture) * moving * _settings.Motion.HoldCarry, 0f, 1f);
        FreeArm = held > 0f ? Math.Clamp(_holds.Sum(h => CharacterRig.FreeArmHolds.Contains(GestureName(h.Key)) ? h.Value : 0f) / MathF.Max(1f, _holds.Values.Sum()), 0f, 1f) : 0f;

        // A gesture that moves the legs too has them while the player stands (and always when it puts them on the ground);
        // walking, the walking legs win.
        LegsWeight = 0f;
        if (_gesture is not null && CharacterRig.LegGestures.Contains(GestureName(_gesture)) && !pose.Seated && gesture > 0f)
        {
            var standing = Grounded ? 1f
                : (_modeWeights[(int)CharacterMode.Ground] + _modeWeights[(int)CharacterMode.Crouch]) * (1f - Math.Clamp(_blendSpeed / _settings.Motion.GestureLegsSpeed, 0f, 1f));
            LegsWeight = gesture * standing;
            if (LegsWeight > 0f)
                AddTo(_legs, _gesture, ClipTime(_gesture, _gestureTime), 1f);
        }
    }

    // Moves a cycle's phase on by distance: at a clip's own speed it plays at its own pace. The clips' shares come from
    // blendSpeed (the eased speed), the pace from the real one, so the feet keep step with the ground.
    private float Advance(float phase, (string Clip, float Speed)[] set, float speed, float blendSpeed, float delta, float minRate = 0f)
    {
        var rate = 0f;
        var total = 0f;
        foreach (var (clip, weight) in Weights(set, blendSpeed))
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

    // skipIdle: the share of idle something else stands in for.
    private void AddBlend((string Clip, float Speed)[] set, float speed, float phase, float layerWeight, float skipIdle = 0f)
    {
        foreach (var (clip, weight) in Weights(set, speed))
        {
            var share = clip == "idle" ? MathF.Max(0f, weight - skipIdle) : weight;
            Add(clip, SpeedOf(clip) > 0f ? phase * _lengths[clip] : _clock, layerWeight * share);
        }
    }

    private static float Smooth(float x) => x * x * (3f - 2f * x);

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
    private static bool IsBucket(CharacterProp prop) => prop is CharacterProp.WaterBucket or CharacterProp.WaterBucketFull or CharacterProp.ChemicalBucketFull;

    private float GestureLength(string clip) =>
        _settings.GestureSeconds.TryGetValue(GestureName(clip), out var seconds) ? seconds : _lengths[clip] * (1f - GestureStart(clip));

    // Where a gesture starts in its clip, as a fraction of it (a first-person version or a variant starts where the clip it
    // stands in for does).
    private float GestureStart(string clip) => _settings.GestureStart.GetValueOrDefault(GestureName(clip));

    // How far into its clip a gesture is after playing for time seconds, in the clip's own seconds.
    private float ClipTime(string clip, float time)
    {
        var start = GestureStart(clip);
        return _lengths[clip] * (start + (1f - start) * time / GestureLength(clip));
    }

    private static string GestureName(string clip) =>
        CharacterRig.GestureOf(clip.EndsWith(CharacterRig.FirstPersonSuffix, StringComparison.Ordinal) ? clip[..^CharacterRig.FirstPersonSuffix.Length] : clip);

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
        clip is "jump" or "land" or "jump_run" or "land_hard" or "idle_look" or "idle_stretch" or "turn_l" or "turn_r"
        || (CharacterRig.ReactionClips.Contains(clip) && !clip.StartsWith("downed", StringComparison.Ordinal))
        || (CharacterRig.UpperBodyClips.Contains(clip) && !clip.StartsWith("hold_", StringComparison.Ordinal));


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
