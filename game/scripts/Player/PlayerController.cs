using Godot;
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

    private const float ThirdPersonDistance = 3.5f;

    private Node3D _head = null!;
    private SpringArm3D _cameraArm = null!;
    private MeshInstance3D _bodyMesh = null!;
    private CollisionShape3D _shape = null!;
    private bool _dead;
    private Func<(Vector3 Feet, Vector3 Velocity)?>? _seat;

    /// <summary>Peer that controls this player (1 is the host).</summary>
    public int PeerId { get; init; } = 1;

    /// <summary>The player's name, from the profile they joined with; shown over their head to others.</summary>
    public string DisplayName { get; set; } = "";

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
    /// A dead player (chunk 20) neither moves nor collides and is not drawn, until it respawns.
    /// </summary>
    public bool Dead
    {
        get => _dead;
        set
        {
            _dead = value;
            Velocity = Vector3.Zero;
            _shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, value || _seat is not null);
            _bodyMesh.Visible = !value && (ThirdPerson || !Camera.Current);
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
        }
    }

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

    /// <summary>Whether the camera is pulled back behind the player.</summary>
    public bool ThirdPerson
    {
        get => _cameraArm.SpringLength > 0f;
        set
        {
            _cameraArm.SpringLength = value ? ThirdPersonDistance : 0f;
            _bodyMesh.Visible = !_dead && (value || !Camera.Current);
        }
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
        _shape = new CollisionShape3D
        {
            Name = "Shape",
            Position = capsuleCentre,
            Shape = new CapsuleShape3D { Height = Settings.Height, Radius = Settings.Radius },
        };
        AddChild(_shape);
        _bodyMesh = new MeshInstance3D
        {
            Name = "BodyMesh",
            Position = capsuleCentre,
            Mesh = new CapsuleMesh { Height = Settings.Height, Radius = Settings.Radius },
        };
        AddChild(_bodyMesh);

        _head = new Node3D { Name = "Head", Position = new Vector3(0, Settings.EyeHeight, 0) };
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
        var input = Step(CurrentInput, (float)delta);
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
        if (_dead)
            return input;
        if (_seat is not null)
        {
            SitStill();
            return input;
        }

        var wasOnFloor = IsOnFloor();
        var velocity = PlayerMotor.Step(Velocity.ToNumerics(), wasOnFloor, input, Settings, delta, SpeedScale);
        Velocity = velocity.ToGodot();
        MoveAndSlide();
        if (!wasOnFloor && IsOnFloor() && velocity.Y < 0)
            Landed?.Invoke(-velocity.Y);
        if (PushesBodies)
            PushRigidBodies(delta);
        return input;
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
