using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Net;
using Mechanica.Core.Player;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Player;

/// <summary>
/// On-foot player body. It never reads devices: whoever owns it (local input, or the host's network
/// input queue) sets <see cref="CurrentInput"/> each physics tick, and the movement itself comes from
/// <see cref="PlayerMotor"/> in Core. The node origin is at the feet.
/// <para>
/// In multiplayer the host simulates every player. A client simulates only its own player (prediction)
/// and shows the others with <see cref="Simulated"/> off, posed from snapshots by <see cref="ApplyState"/>.
/// </para>
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    /// <summary>Godot path of the default movement tuning.</summary>
    public const string DefaultSettingsPath = "res://data/player.json";

    private Node3D _head = null!;
    private SpringArm3D _cameraArm = null!;
    private MeshInstance3D _bodyMesh = null!;
    private CapsuleShape3D _capsule = null!;
    private CapsuleShape3D _headroom = null!;
    private bool _crouched;
    private float _eyeHeight;
    private Node3D? _character;
    private CollisionShape3D _shape = null!;
    private bool _dead;
    private bool _deathCamera;
    private Node3D? _arms;
    private CharacterGesture _gesture;
    private byte _gestureCount;
    private Func<(Vector3 Feet, Vector3 Velocity)?>? _seat;
    // The seat transition (UI spec §9): where the body was shown when it started, and how long is left.
    private Vector3 _shownFrom;
    private double _transitionLeft;
    private double _sinceSeatChange = double.MaxValue;
    private Vector3 _lastShown;
    private Vector3 _knock;
    // The camera's jolt from a blow: a kick (pitch, roll in radians) springing back, and a shake fading out.
    private Vector2 _kick;
    private Vector2 _kickRate;
    private float _shake;
    private float _shakeTime;

    /// <summary>Peer that controls this player (1 is the host).</summary>
    public int PeerId { get; init; } = 1;

    /// <summary>The player's name, from the profile they joined with; shown over their head to others.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// What an AI agent's player says it is doing (<c>SetThought</c>), or "" (people, and agents that said
    /// nothing). Synced by <see cref="Net.PlayerSync"/>; the spectator camera and name tags show it.
    /// </summary>
    public string Thought { get; set; } = "";

    /// <summary>When false the body does not move itself; something else poses it each tick.</summary>
    public bool Simulated { get; set; } = true;

    /// <summary>
    /// What walking and running speed are multiplied by, 0 to 1: below 1 while a bot's bubble wrap slows the
    /// player. The host sets it from <c>SurvivalActions.SpeedScale</c>, a client from its vitals, so
    /// prediction agrees with the host.
    /// </summary>
    public float SpeedScale { get; set; } = 1f;

    /// <summary>Whether walking into rigid bodies pushes them. Only the host's simulation pushes.</summary>
    public bool PushesBodies { get; set; } = true;

    /// <summary>Raised after each physics tick the player simulated, with the sanitized input it used.</summary>
    public event Action<PlayerInput>? Stepped;

    /// <summary>Raised when the player lands on the floor after falling, with the downward speed it hit at (m/s).</summary>
    public event Action<float>? Landed;

    /// <summary>
    /// A dead player (chunk 20) neither moves nor collides until it respawns; its body lies where it fell. The local
    /// player's camera pulls back to third person to see it, and returns to first person on respawn.
    /// </summary>
    public bool Dead
    {
        get => _dead;
        set
        {
            if (value != _dead && Camera.Current)
            {
                if (value && !ThirdPerson)
                {
                    _deathCamera = true;
                    ThirdPerson = true;
                }
                else if (!value && _deathCamera)
                {
                    _deathCamera = false;
                    CameraDistance = 0f;
                }
            }
            _dead = value;
            Velocity = Vector3.Zero;
            _shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, value || _seat is not null);
            UpdateBodyVisibility();
        }
    }

    /// <summary>
    /// While the player sits in a seat (chunk 10): where its feet go this tick and how fast the seat moves,
    /// or null once the seat is gone. A seated player does not walk or collide; its input still flows to
    /// the host, which hands it to the seat's script. Null on foot.
    /// </summary>
    public Func<(Vector3 Feet, Vector3 Velocity)?>? Seat
    {
        get => _seat;
        set
        {
            if (_seat is null == value is null)
            {
                _seat = value;
                return;
            }
            _seat = value;
            Velocity = Vector3.Zero;
            _shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, value is not null || _dead);
            // Sitting down glides from where it stood; standing up glides once the host puts it beside the seat (_Process).
            if (value is not null)
                BeginTransition(ShownPosition);
            _sinceSeatChange = 0;
        }
    }

    /// <summary>
    /// Where the body is drawn: its position, or on its way there while it sits down or stands up (it glides over
    /// <see cref="PlayerMovementSettings.SeatTransitionSeconds"/> instead of jumping).
    /// </summary>
    public Vector3 ShownPosition => GlobalPosition + ShownOffset;

    /// <summary>True while the body glides into or out of a seat.</summary>
    public bool InTransition => _transitionLeft > 0;

    private Vector3 ShownOffset
    {
        get
        {
            if (_transitionLeft <= 0 || Settings is null)
                return Vector3.Zero;
            var t = 1f - (float)(_transitionLeft / Settings.SeatTransitionSeconds);
            var eased = t * t * (3f - 2f * t);
            return _shownFrom.Lerp(GlobalPosition, eased) - GlobalPosition;
        }
    }

    private void BeginTransition(Vector3 from)
    {
        if (Settings is null || !IsInsideTree())
            return;
        _shownFrom = from;
        _transitionLeft = Settings.SeatTransitionSeconds;
    }

    /// <summary>True when a standing player's capsule at <paramref name="feet"/> touches nothing (this player aside).</summary>
    public bool RoomAt(Vector3 feet)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D { Height = Settings.Height, Radius = Settings.Radius },
            Transform = new Transform3D(Basis.Identity, feet + new Vector3(0, Settings.Height / 2f, 0)),
            CollisionMask = CollisionMask,
            Exclude = [GetRid()],
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    public override void _Process(double delta)
    {
        // Standing up, the host moves the player beside the seat a moment after the seat lets go: glide from where it was.
        _sinceSeatChange += delta;
        if (_sinceSeatChange < 0.6 && !Seated && _lastShown.DistanceTo(GlobalPosition) > 0.3f && _transitionLeft <= 0)
            BeginTransition(_lastShown);
        if (_transitionLeft > 0)
            _transitionLeft = Math.Max(0, _transitionLeft - delta);
        ShownLocalOffset = Basis.Inverse() * ShownOffset;
        _head.Position = new Vector3(0, _eyeHeight, 0) + ShownLocalOffset;
        _bodyMesh.Position = _shape.Position + ShownLocalOffset;
        _lastShown = ShownPosition;
        StepJolt((float)delta);
    }

    // How hard each reaction jolts the camera: kick (radians) and shake (radians at its peak).
    private static (float Kick, float Shake) JoltOf(HitReaction reaction) => reaction switch
    {
        HitReaction.Light => (0.05f, 0.012f),
        HitReaction.Heavy => (0.11f, 0.03f),
        HitReaction.Knockdown => (0.16f, 0.05f),
        _ => (0f, 0f),
    };

    /// <summary>
    /// Jolts the camera with a blow (the local player's own): it kicks away from <paramref name="from"/> (radians from where the
    /// player looks, positive to the left; null kicks straight back), springs back and shakes for a moment.
    /// </summary>
    public void Jolt(HitReaction reaction, float? from)
    {
        var (kick, shake) = JoltOf(reaction);
        var angle = from ?? 0f;
        // A blow from the front tips the head back (pitch up), from the side rolls it away.
        _kickRate += new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * kick * 40f;
        _shake = Mathf.Max(_shake, shake);
        _shakeTime = 0f;
    }

    private void StepJolt(float delta)
    {
        if (_kick == Vector2.Zero && _kickRate == Vector2.Zero && _shake <= 0f)
            return;
        // A stiff, well damped spring: the kick peaks in a few hundredths of a second and settles in about a third.
        _kickRate += (-_kick * 220f - _kickRate * 24f) * delta;
        _kick += _kickRate * delta;
        _shakeTime += delta;
        _shake = Mathf.Max(0f, _shake - delta * 0.12f);
        var shake = new Vector2(Mathf.Sin(_shakeTime * 61f), Mathf.Sin(_shakeTime * 47f + 1.3f)) * _shake;
        if (_kick.LengthSquared() < 1e-8f && _kickRate.LengthSquared() < 1e-6f && _shake <= 0f)
            _kick = _kickRate = shake = Vector2.Zero;
        Camera.Rotation = new Vector3(_kick.X + shake.X, 0f, _kick.Y + shake.Y);
    }

    /// <summary>How far the drawn body is from the player's position, in its own frame (the character model adds it).</summary>
    public Vector3 ShownLocalOffset { get; private set; }

    /// <summary>True while the player sits in a seat.</summary>
    public bool Seated => _seat is not null;

    /// <summary>The sanitized input of the last tick the player stepped (the host reads a seated player's steering here).</summary>
    public PlayerInput LastInput { get; private set; }

    /// <summary>Movement tuning. Set before the node enters the tree to override the default file.</summary>
    public PlayerMovementSettings Settings { get; set; } = null!;

    /// <summary>Input for the next physics tick. It is sanitized before use.</summary>
    public PlayerInput CurrentInput { get; set; }

    /// <summary>The first-person/third-person camera. Only the local player's camera is current.</summary>
    public Camera3D Camera { get; private set; } = null!;

    /// <summary>Pivot at eye height that carries the look pitch.</summary>
    public Node3D Head => _head;

    /// <summary>True while the player is crouched: its capsule is <see cref="PlayerMovementSettings.CrouchHeight"/> tall.</summary>
    public bool Crouched => _crouched;

    /// <summary>The collision capsule's current height in metres.</summary>
    public float CapsuleHeight => _capsule.Height;

    /// <summary>
    /// Whether the camera is pulled back behind the player. Setting it true puts the camera at the default
    /// third-person distance (<see cref="PlayerMovementSettings.CameraDistance"/>, or the closest when that is 0).
    /// </summary>
    public bool ThirdPerson
    {
        get => _cameraArm.SpringLength > 0f;
        set => CameraDistance = value ? Settings.CameraDistance > 0 ? Settings.CameraDistance : Settings.CameraMinDistance : 0f;
    }

    /// <summary>
    /// What the field of view is multiplied by: below 1 while a weapon is aimed (the slight zoom of UI spec §10,
    /// the weapon's <c>aim_zoom</c>). The local camera eases towards it.
    /// </summary>
    public float AimZoom { get; set; } = 1f;

    /// <summary>How far behind the player's head the camera sits, in metres; 0 is first-person (UI spec §6, X and C zoom).</summary>
    public float CameraDistance
    {
        get => _cameraArm.SpringLength;
        set
        {
            _cameraArm.SpringLength = Mathf.Max(0f, value);
            // Over the shoulder in third person, easing in as the camera closes on the head.
            var share = Settings.CameraMinDistance > 0 ? Mathf.Min(1f, _cameraArm.SpringLength / Settings.CameraMinDistance) : 1f;
            _cameraArm.Position = new Vector3(Settings.CameraShoulder, Settings.CameraRaise, 0) * share;
            UpdateBodyVisibility();
        }
    }

    /// <summary>
    /// Where aim rays start: the point on the camera's line of sight level with the player's head, so a
    /// third-person camera reaches as far as a first-person one and never aims at what is behind the player.
    /// </summary>
    public Vector3 AimOrigin
    {
        get
        {
            var camera = Camera.GlobalTransform;
            var forward = -camera.Basis.Z;
            return camera.Origin + forward * Mathf.Max(0f, forward.Dot(_head.GlobalPosition - camera.Origin));
        }
    }

    /// <summary>
    /// Where the water surface is (world Y), or null when there is no water. Set by whoever owns the world; the
    /// player swims when deep enough below it.
    /// </summary>
    public Func<float>? WaterLevel { get; set; }

    /// <summary>How far the feet are below the water surface in metres (0 or less: out of the water).</summary>
    public float WaterDepth => WaterLevel is { } level ? level() - GlobalPosition.Y : 0f;

    /// <summary>
    /// What this player holds, as others see it. The local player's tools set it; it travels with the input,
    /// so the host and other players see it too.
    /// </summary>
    public CharacterProp HeldProp { get; set; }

    /// <summary>
    /// A prop shown instead of <see cref="HeldProp"/> while an action needs its own in hand (the Refine Tool while Use
    /// is held on a resource node), or null. It travels with the input like <see cref="HeldProp"/>.
    /// </summary>
    public CharacterProp? ActionProp { get; set; }

    /// <summary>
    /// True while this player holds the tool in hand raised (the Sledgehammer up to block, the Connect Tool over a node,
    /// the Paint Tool spraying); travels with the input like <see cref="HeldProp"/>.
    /// </summary>
    public bool HeldGuard { get; set; }

    /// <summary>
    /// The Paint Tool's colour, as a palette index, with <see cref="CharacterRig.TintErase"/> set while it takes paint off;
    /// travels with the input like <see cref="HeldProp"/>, so every peer tints the tool and its mist alike.
    /// </summary>
    public byte HeldTint { get; set; }

    /// <summary>The Paint Tool colour shown after the last tick or snapshot (<see cref="HeldTint"/>).</summary>
    public byte ShownTint { get; private set; }

    /// <summary>How the body stands after the last tick (or snapshot, for players this peer does not simulate).</summary>
    public BodyFlags Body { get; private set; } = BodyFlags.OnFloor;

    /// <summary>The prop shown in hand after the last tick or snapshot.</summary>
    public CharacterProp ShownProp { get; private set; }

    /// <summary>The newest gesture after the last tick or snapshot, and its count.</summary>
    public (CharacterGesture Gesture, byte Count) ShownGesture { get; private set; }

    /// <summary>The character model drawn for this player, or null while it is drawn as a capsule.</summary>
    public Node3D? Character => _character;

    /// <summary>Everything the character's animation needs this frame.</summary>
    public CharacterPose Pose => new(
        Velocity.ToNumerics(), SeatYaw ?? Rotation.Y, _head.Rotation.X,
        Body.HasFlag(BodyFlags.OnFloor), Body.HasFlag(BodyFlags.Swimming), Body.HasFlag(BodyFlags.Crawl), Seated || Body.HasFlag(BodyFlags.Seated),
        ShownProp, ShownGesture.Gesture, ShownGesture.Count, Body.HasFlag(BodyFlags.Guard), Seated ? SeatStyle : null, _dead);

    /// <summary>How the seat the player sits in holds it (its legs, hands and bounce); set with <see cref="Seat"/>.</summary>
    public SeatStyle SeatStyle { get; set; } = SeatStyle.Default;

    /// <summary>The yaw (radians, 0 faces -Z) of the seat's front this frame, or null; set with <see cref="Seat"/>.</summary>
    public Func<float?>? SeatFacing { get; set; }

    /// <summary>Which way a seated body faces: the seat's front, whichever way the player looks. Null on foot.</summary>
    public float? SeatYaw => Seated ? SeatFacing?.Invoke() : null;

    /// <summary>
    /// Plays a gesture on this player (the local player's tools and items call it). Like <see cref="HeldProp"/> it
    /// travels with the input.
    /// </summary>
    public void Gesture(CharacterGesture gesture)
    {
        if (gesture == CharacterGesture.None)
            return;
        _gesture = gesture;
        _gestureCount++;
    }

    /// <summary>Puts <paramref name="arms"/> (first-person arms, or null for none) on the camera, replacing any before.</summary>
    public void SetArms(Node3D? arms)
    {
        if (_arms == arms)
            return;
        _arms?.QueueFree();
        _arms = arms;
        if (arms is not null)
            Camera.AddChild(arms);
    }

    /// <summary>The first-person arms on the camera, or null.</summary>
    public Node3D? Arms => _arms;

    /// <summary>Draws <paramref name="model"/> as this player instead of the capsule (null goes back to the capsule).</summary>
    public void SetCharacter(Node3D? model)
    {
        if (_character == model)
            return;
        if (_character is not null)
        {
            RemoveChild(_character);
            _character.QueueFree();
        }
        _character = model;
        if (model is not null)
            AddChild(model);
        UpdateBodyVisibility();
    }

    /// <summary>True when this player's body is drawn: whenever it is not seen from inside its own head (a dead body lies where it fell).</summary>
    public bool BodyVisible => ThirdPerson || !Camera.Current;

    /// <summary>
    /// True while the local player looks out of a seat's interior camera from its own eyes: the seated body is drawn
    /// (legs, hands on the wheel) without its head, which would fill the view.
    /// </summary>
    public bool HeadHidden
    {
        get => _headHidden;
        set
        {
            _headHidden = value;
            UpdateBodyVisibility();
        }
    }

    private bool _headHidden;

    private void UpdateBodyVisibility()
    {
        _bodyMesh.Visible = _character is null && BodyVisible;
        if (_character is not null)
            _character.Visible = BodyVisible;
    }

    /// <summary>Loads <see cref="DefaultSettingsPath"/>, failing loudly on bad data.</summary>
    public static PlayerMovementSettings LoadDefaultSettings()
    {
        using var file = FileAccess.Open(DefaultSettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {DefaultSettingsPath}: {FileAccess.GetOpenError()}");
        return PlayerMovementSettings.FromJson(file.GetAsText());
    }

    public override void _Ready()
    {
        Settings ??= LoadDefaultSettings();
        FloorMaxAngle = Mathf.DegToRad(45f);
        FloorSnapLength = 0.2f;

        var capsuleCentre = new Vector3(0, Settings.Height / 2f, 0);
        _capsule = new CapsuleShape3D { Height = Settings.Height, Radius = Settings.Radius };
        _shape = new CollisionShape3D { Name = "Shape", Position = capsuleCentre, Shape = _capsule };
        // The space a standing player needs above a crouching one, a little narrower so walls beside it don't count.
        var headroomRadius = Settings.Radius - 0.05f;
        _headroom = new CapsuleShape3D
        {
            Radius = headroomRadius,
            Height = Mathf.Min(Settings.Height - Settings.CrouchHeight + 2f * headroomRadius, Settings.Height - 0.05f),
        };
        AddChild(_shape);
        _bodyMesh = new MeshInstance3D
        {
            Name = "BodyMesh",
            Position = capsuleCentre,
            Mesh = new CapsuleMesh { Height = Settings.Height, Radius = Settings.Radius },
        };
        AddChild(_bodyMesh);

        _eyeHeight = Settings.EyeHeight;
        _head = new Node3D { Name = "Head", Position = new Vector3(0, _eyeHeight, 0) };
        AddChild(_head);
        _cameraArm = new SpringArm3D { Name = "CameraArm", SpringLength = 0f, Margin = 0.1f };
        _cameraArm.AddExcludedObject(GetRid());
        _head.AddChild(_cameraArm);
        Camera = new Camera3D { Name = "Camera", Near = 0.05f, Fov = 75f };
        _cameraArm.AddChild(Camera);
    }

    /// <summary>Makes this player's camera the active one (first person, body hidden).</summary>
    public void MakeCameraCurrent()
    {
        Camera.MakeCurrent();
        ThirdPerson = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Simulated)
            return;
        var input = Step(CurrentInput with { Prop = ActionProp ?? HeldProp, Gesture = _gesture, GestureCount = _gestureCount, Guard = HeldGuard, Tint = HeldTint }, (float)delta);
        Stepped?.Invoke(input);
    }

    /// <summary>
    /// Simulates one tick of <paramref name="input"/> and returns the sanitized input used. Called by
    /// <see cref="_PhysicsProcess"/>, and directly when a client replays inputs after a correction.
    /// </summary>
    public PlayerInput Step(PlayerInput input, float delta)
    {
        input = input.Sanitized(Settings);
        LastInput = input;
        ApplyLook(input.Yaw, input.Pitch);
        ShownProp = input.Prop;
        ShownGesture = (input.Gesture, input.GestureCount);
        ShownTint = input.Tint;
        if (_dead)
            return input;
        if (_seat is not null)
        {
            SitStill();
            Body = BodyFlags.Seated;
            return input;
        }

        var wasOnFloor = IsOnFloor();
        var depth = WaterDepth;
        var swimming = PlayerMotor.IsSwimming(depth, Settings);
        SetCrouched(PlayerMotor.Crouched(input.Crawl, _crouched, !_crouched || RoomToStand(), swimming));
        // Kept down under a low ceiling, the player moves at crouching speed whatever it holds.
        var move = swimming ? input : input with { Crawl = _crouched };
        var velocity = PlayerMotor.Step(Velocity.ToNumerics(), wasOnFloor, move, Settings, delta, SpeedScale, depth);
        // A knockback lands once, after the walk, so its lift takes the player off the floor.
        velocity += _knock.ToNumerics();
        _knock = Vector3.Zero;
        Velocity = velocity.ToGodot();
        MoveAndSlide();
        if (!wasOnFloor && IsOnFloor() && velocity.Y < 0)
            Landed?.Invoke(-velocity.Y);
        if (PushesBodies)
            PushRigidBodies(delta);
        Body = (IsOnFloor() ? BodyFlags.OnFloor : 0)
            | (PlayerMotor.IsSwimming(WaterDepth, Settings) ? BodyFlags.Swimming : 0)
            | (_crouched ? BodyFlags.Crawl : 0)
            | (input.Guard ? BodyFlags.Guard : 0);
        StepEyeHeight(delta);
        return input;
    }

    /// <summary>
    /// Adds <paramref name="velocity"/> (m/s) to the player's movement on its next step: a knockback (a Sledgehammer
    /// hit). A seated or dead player ignores it.
    /// </summary>
    public void Knock(Vector3 velocity)
    {
        if (!_dead && _seat is null && velocity.IsFinite())
            _knock += velocity;
    }

    /// <summary>Puts a seated player's feet on its seat. False when it sits nowhere (or the seat is gone).</summary>
    public bool SitStill()
    {
        if (_seat?.Invoke() is not { } at)
            return false;
        GlobalPosition = at.Feet;
        Velocity = at.Velocity;
        return true;
    }

    /// <summary>Poses the player from a host snapshot (remote players on clients).</summary>
    public void ApplyState(Vector3 position, Vector3 velocity, float yaw, float pitch)
    {
        GlobalPosition = position;
        Velocity = velocity;
        ApplyLook(yaw, pitch);
    }

    /// <summary>Shows how a player this peer does not simulate stands and what they hold, from a host snapshot.</summary>
    public void ApplyBody(BodyFlags body, CharacterProp prop, CharacterGesture gesture, byte gestureCount, byte tint = 0)
    {
        ShownTint = tint;
        Body = body;
        SetCrouched(body.HasFlag(BodyFlags.Crawl));
        StepEyeHeight((float)GetPhysicsProcessDeltaTime());
        ShownProp = prop;
        ShownGesture = (gesture, gestureCount);
    }

    // Shrinks or restores the capsule, keeping the feet where they are.
    private void SetCrouched(bool crouched)
    {
        if (crouched == _crouched)
            return;
        _crouched = crouched;
        var height = crouched ? Settings.CrouchHeight : Settings.Height;
        _capsule.Height = height;
        _shape.Position = new Vector3(0, height / 2f, 0);
        if (_bodyMesh.Mesh is CapsuleMesh mesh)
            mesh.Height = height;
        _bodyMesh.Position = _shape.Position;
    }

    private void StepEyeHeight(float delta)
    {
        _eyeHeight = PlayerMotor.EyeHeight(_eyeHeight, _crouched, Settings, delta);
        _head.Position = new Vector3(0, _eyeHeight, 0);
    }

    // True when nothing is in the space a standing player would fill above the crouched capsule.
    private bool RoomToStand()
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _headroom,
            Transform = new Transform3D(Basis.Identity, GlobalPosition + new Vector3(0, Settings.Height - _headroom.Height / 2f, 0)),
            CollisionMask = CollisionMask,
            Exclude = [GetRid()],
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    private void ApplyLook(float yaw, float pitch)
    {
        Rotation = new Vector3(0, yaw, 0);
        _head.Rotation = new Vector3(pitch, 0, 0);
    }

    private void PushRigidBodies(float delta)
    {
        for (var i = 0; i < GetSlideCollisionCount(); i++)
        {
            var collision = GetSlideCollision(i);
            if (collision.GetCollider() is not RigidBody3D body)
                continue;
            var push = -collision.GetNormal();
            push.Y = 0f;
            if (push.LengthSquared() < 1e-4f)
                continue;
            body.ApplyImpulse(push.Normalized() * Settings.PushForce * delta,
                collision.GetPosition() - body.GlobalPosition);
        }
    }
}
