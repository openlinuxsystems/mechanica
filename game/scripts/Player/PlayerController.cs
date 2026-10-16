using Godot;
using Mechanica.Core.Player;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Player;

/// <summary>
/// On-foot player body. It never reads devices: whoever owns it (local input today, host-side
/// network input in chunk 2b) sets <see cref="CurrentInput"/> each physics tick, and the movement
/// itself comes from <see cref="PlayerMotor"/> in Core. The node origin is at the feet.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    /// <summary>Godot path of the default movement tuning.</summary>
    public const string DefaultSettingsPath = "res://data/player.json";

    private const float ThirdPersonDistance = 3.5f;

    private Node3D _head = null!;
    private SpringArm3D _cameraArm = null!;
    private MeshInstance3D _bodyMesh = null!;

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
            _bodyMesh.Visible = value || !Camera.Current;
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
        AddChild(new CollisionShape3D
        {
            Name = "Shape",
            Position = capsuleCentre,
            Shape = new CapsuleShape3D { Height = Settings.Height, Radius = Settings.Radius },
        });
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
        var input = CurrentInput.Sanitized(Settings);
        Rotation = new Vector3(0, input.Yaw, 0);
        _head.Rotation = new Vector3(input.Pitch, 0, 0);

        var velocity = PlayerMotor.Step(Velocity.ToNumerics(), IsOnFloor(), input, Settings, (float)delta);
        Velocity = velocity.ToGodot();
        MoveAndSlide();
        PushRigidBodies((float)delta);
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
