using Godot;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Game.Creations;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// A player's Lift in the world: the hydraulic lift model (<see cref="LiftSettings.Model"/>), which players can stand on
/// and which a held Creation rests on. <see cref="Show"/> moves it to a <see cref="LiftState"/>. Every peer animates it the
/// same way from the state alone: put down somewhere new it plays the model's <c>deploy</c> clip (outriggers out, column up
/// to the resting height, lights flash), and a new level eases the deck there (<see cref="LiftMotion"/>) by scrubbing the
/// <c>extend</c> clip, whose time is the deck height in metres. The collision follows the deck as shown, and a held
/// Creation is drawn riding the deck (<see cref="CreationNode.VisualOffset"/>) while its bodies stay where the host put them.
/// While the column moves each peer plays the same sounds from the motion alone: <c>lift_raise</c> or <c>lift_lower</c>
/// as it sets off, the <c>lift_motor</c> loop (its <c>speed</c> following the deck's) until it stops, and the end-stop
/// clunk at the bottom or the top. Without the model (a test mod) a plain block stands in.
/// </summary>
public partial class LiftBody : StaticBody3D
{
    private const string LightMaterial = "lift_light";
    private const float PlatformThickness = 0.11f;
    private const float BaseHeight = 0.22f;
    private const float BaseSize = 0.86f;
    private const float ColumnRadius = 0.14f;
    private const float RiderMargin = 0.1f;
    private const float RiderAbove = 0.35f;
    private const float RiderSnap = 0.1f;

    private static readonly StandardMaterial3D FallbackMaterial = new() { AlbedoColor = UiTokens.Blue };

    private readonly BoxShape3D _platform = new();
    private readonly CylinderShape3D _column = new() { Radius = ColumnRadius };
    private CollisionShape3D _columnShape = null!;
    private CollisionShape3D _platformShape = null!;
    private readonly List<(StandardMaterial3D Material, float Energy)> _lights = [];
    private readonly Dictionary<Animation, List<(int Track, Node3D Node, Animation.TrackType Type)>> _tracks = [];
    private Node3D? _model;
    private Node3D? _deck;
    private Node3D? _columnDrive;
    private Node3D? _lightDrive;
    private Animation? _deploy;
    private Animation? _extend;
    private MeshInstance3D? _fallback;
    private LiftSettings _settings = new();
    private LiftMotion? _motion;
    private double _deployTime;
    private int? _heldShown;
    private Sound.SoundPlayer.Voice? _motor;

    /// <summary>Loads the model; null shows the plain block.</summary>
    public PartAssets? Assets { get; init; }

    /// <summary>Finds a Creation's node, to draw the held one riding the deck.</summary>
    public Func<int, CreationNode?>? FindCreation { get; init; }

    /// <summary>Plays the end-stop sound on this peer; null plays none.</summary>
    public Sound.SoundSync? Sounds { get; init; }

    /// <summary>The players this peer moves (on the host, every player); those standing on the deck are kept on it as it moves.</summary>
    public Func<IEnumerable<Node3D>>? Riders { get; init; }

    /// <summary>The Lift's state as last shown.</summary>
    public LiftState State { get; private set; } = null!;

    /// <summary>Deck height shown now, in metres above the Lift's base (it eases towards <see cref="LiftSettings.TopAt"/>).</summary>
    public float ShownTop { get; private set; }

    /// <summary>True once the deploy animation has finished (at once without the model).</summary>
    public bool Deployed => _deploy is null || _deployTime >= _deploy.Length;

    /// <summary>How fast the deck is moving now, in metres per second (positive going up).</summary>
    public float Speed => _motion?.Velocity ?? 0f;

    /// <summary>True while the column is moving between levels.</summary>
    public bool Moving => _motion?.Moving ?? false;

    /// <summary>How bright the amber lights are now, 0 to 1.</summary>
    public float LightLevel { get; private set; }

    /// <summary>The model's root, or null when it did not load.</summary>
    public Node3D? Model => _model;

    /// <summary>World position of the middle of the deck as shown now.</summary>
    public Vector3 Top => GlobalPosition + new Vector3(0, ShownTop, 0);

    /// <summary>
    /// True when feet at <paramref name="feet"/> (world space) stand on the deck as shown: over the platform and at its
    /// height. Such a player rides the deck up and down.
    /// </summary>
    public bool Carries(Vector3 feet)
    {
        if (State is null)
            return false;
        var local = feet - GlobalPosition;
        var half = _settings.PlatformSize / 2 + RiderMargin;
        return Math.Abs(local.X) <= half && Math.Abs(local.Z) <= half && local.Y > ShownTop - RiderMargin && local.Y < ShownTop + RiderAbove;
    }

    public override void _Ready()
    {
        AddChild(new CollisionShape3D { Name = "Base", Shape = new BoxShape3D { Size = new Vector3(BaseSize, BaseHeight, BaseSize) }, Position = new Vector3(0, BaseHeight / 2, 0) });
        AddChild(_columnShape = new CollisionShape3D { Name = "Column", Shape = _column });
        AddChild(_platformShape = new CollisionShape3D { Name = "Shape", Shape = _platform });
    }

    public override void _ExitTree()
    {
        Hold(null, 0f);
        StopMotor();
    }

    /// <summary>The motor loop playing while the column moves, or null.</summary>
    public Sound.SoundPlayer.Voice? Motor => _motor;

    // The column's sounds: setting off it kicks in (raise or lower) and the motor runs, its pitch and volume following
    // the deck's speed, until it comes to rest.
    private void RunMotor()
    {
        if (Sounds is null)
            return;
        var moving = _motion!.Moving && (_deploy is null || _deployTime >= _deploy.Length);
        if (!moving)
        {
            StopMotor();
            return;
        }
        if (_motor is null)
        {
            Sounds.PlayLocal(_motion.Velocity > 0 ? "lift_raise" : "lift_lower", Identifiers.VanillaModId, Top);
            if (Sounds.Registry.Find("lift_motor", Identifiers.VanillaModId) is { } loop)
                _motor = Sounds.Player.Play(loop, Top, () => IsInstanceValid(this) && IsInsideTree() ? Top : null);
        }
        _motor?.SetParameter("speed", Math.Clamp(Math.Abs(_motion.Velocity) / Math.Max(1e-3f, _settings.RaiseSpeed), 0f, 1f));
    }

    private void StopMotor()
    {
        if (_motor is not null && Sounds is not null && IsInstanceValid(Sounds))
            Sounds.Player.Stop(_motor);
        _motor = null;
    }

    /// <summary>Places the Lift for <paramref name="state"/>: put down somewhere new it deploys; a new level eases there.</summary>
    public void Show(LiftState state, LiftSettings settings)
    {
        var moved = State is null || State.Position != state.Position;
        _settings = settings;
        State = state;
        _platform.Size = new Vector3(settings.PlatformSize, PlatformThickness, settings.PlatformSize);
        if (_model is null && _fallback is null)
            LoadModel();
        Position = state.Position.ToGodot();
        if (moved)
        {
            _deployTime = 0;
            var start = _deploy is not null ? DriveAt(0) : settings.TopAt(0);
            _motion = new LiftMotion(settings, start);
        }
        Pose(0);
    }

    // On the physics tick, like everything else that moves, so the deck's collision and the players riding it move together.
    public override void _PhysicsProcess(double delta)
    {
        if (State is not null)
            Pose(delta);
    }

    private void LoadModel()
    {
        _model = Assets?.InstantiateScene(_settings.Model, Identifiers.VanillaModId);
        if (_model is null)
        {
            _fallback = new MeshInstance3D { Name = "Mesh", Mesh = new BoxMesh { Material = FallbackMaterial } };
            AddChild(_fallback);
            return;
        }
        _model.Name = "Model";
        AddChild(_model);
        if (FindPlayer(_model) is { } player)
        {
            _deploy = player.HasAnimation("deploy") ? player.GetAnimation("deploy") : null;
            _extend = player.HasAnimation("extend") ? player.GetAnimation("extend") : null;
            player.GetParent().RemoveChild(player);
            player.QueueFree();
        }
        _deck = _model.FindChild("Deck", true, false) as Node3D;
        _columnDrive = _model.FindChild("ColumnDrive", true, false) as Node3D;
        _lightDrive = _model.FindChild("LightDrive", true, false) as Node3D;
        if (_columnDrive is null)
            _deploy = null;
        CollectLights(_model);
    }

    /// <summary>The first <see cref="AnimationPlayer"/> at or under <paramref name="node"/>, or null.</summary>
    public static AnimationPlayer? FindPlayer(Node node)
    {
        if (node is AnimationPlayer player)
            return player;
        foreach (var child in node.GetChildren())
        {
            if (FindPlayer(child) is { } found)
                return found;
        }
        return null;
    }

    // Each Lift gets its own copy of the light material, so its lights flash on their own.
    private void CollectLights(Node node)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
        {
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                if (mesh.SurfaceGetMaterial(i) is StandardMaterial3D { ResourceName: LightMaterial } material)
                {
                    var own = (StandardMaterial3D)material.Duplicate();
                    instance.SetSurfaceOverrideMaterial(i, own);
                    _lights.Add((own, Math.Max(material.EmissionEnergyMultiplier, 1f)));
                }
            }
        }
        foreach (var child in node.GetChildren())
            CollectLights(child);
    }

    private float DriveAt(double time)
    {
        Apply(_deploy!, time);
        return _columnDrive!.Position.Y;
    }

    private void Pose(double delta)
    {
        var target = _settings.TopAt(State.Level);
        if (_deploy is not null && _deployTime < _deploy.Length)
        {
            _deployTime = Math.Min(_deploy.Length, _deployTime + delta);
            ShownTop = DriveAt(_deployTime);
            _motion!.Reset(ShownTop);
        }
        else if (_motion!.Advance(delta, target) && (State.Level == 0 || State.Level == _settings.MaxLevel))
            Sounds?.PlayLocal(_settings.EndStopSound, Identifiers.VanillaModId, GlobalPosition + new Vector3(0, target, 0));
        if (_deploy is null || _deployTime >= _deploy.Length)
            ShownTop = _motion.Height;
        RunMotor();
        // Players standing on the deck move with it (a character takes its floor's velocity).
        ConstantLinearVelocity = new Vector3(0, _motion.Velocity, 0);
        if (_extend is not null)
            Apply(_extend, ShownTop);
        if (_fallback is not null)
        {
            ((BoxMesh)_fallback.Mesh).Size = new Vector3(_settings.PlatformSize, ShownTop, _settings.PlatformSize);
            _fallback.Position = new Vector3(0, ShownTop / 2, 0);
        }
        LightLevel = _lightDrive?.Position.Y ?? 0f;
        foreach (var (material, energy) in _lights)
            material.EmissionEnergyMultiplier = energy * Mathf.Lerp(0.08f, 1f, Math.Clamp(LightLevel, 0f, 1f));
        _platformShape.Position = new Vector3(0, ShownTop - PlatformThickness / 2, 0);
        var column = Math.Max(0.01f, ShownTop - PlatformThickness - BaseHeight);
        _column.Height = column;
        _columnShape.Position = new Vector3(0, BaseHeight + column / 2, 0);
        CarryRiders();
        // The deck as drawn (the model's settle and bounce included) against where the host put the held Creation.
        var deck = _deck is not null && _deck.IsInsideTree() ? _deck.GlobalPosition.Y - GlobalPosition.Y : ShownTop;
        Hold(State.Creation, deck - target);
    }

    // Keeps whoever stands on the deck on it. Their own movement only follows the deck a tick late, and a client's
    // replay after a correction starts from where the host had them a few ticks back: rising, they would sink in, and
    // lowering they would hang above it and drop. A rider going up faster than the deck (jumping) is left alone.
    private void CarryRiders()
    {
        var top = GlobalPosition.Y + ShownTop;
        foreach (var rider in Riders?.Invoke() ?? [])
        {
            var feet = rider.GlobalPosition;
            if (!Carries(feet))
                continue;
            var above = feet.Y - top;
            var rising = rider is CharacterBody3D body && body.Velocity.Y > Math.Max(Speed, 0f) + 1e-3f;
            if (above < 0f || (Speed < 0f && above < RiderSnap && !rising))
                rider.GlobalPosition = feet with { Y = top };
        }
    }

    private void Hold(int? creation, float offset)
    {
        if (_heldShown is { } old && old != creation && FindCreation?.Invoke(old) is { } dropped)
            dropped.VisualOffset = Vector3.Zero;
        _heldShown = creation;
        if (creation is { } id && FindCreation?.Invoke(id) is { } node)
            node.VisualOffset = new Vector3(0, Math.Abs(offset) < 1e-4f ? 0f : offset, 0);
    }

    // Sets every node a clip animates to its value at `time` (the clips only move, turn and scale nodes).
    private void Apply(Animation clip, double time)
    {
        if (!_tracks.TryGetValue(clip, out var tracks))
        {
            tracks = [];
            for (var track = 0; track < clip.GetTrackCount(); track++)
            {
                if (_model!.GetNodeOrNull<Node3D>(new NodePath(clip.TrackGetPath(track).GetConcatenatedNames())) is { } target)
                    tracks.Add((track, target, clip.TrackGetType(track)));
            }
            _tracks[clip] = tracks;
        }
        foreach (var (track, node, type) in tracks)
            ApplyTrack(clip, track, node, type, time);
    }

    /// <summary>
    /// A new copy of the Lift's model standing still: folded as its <c>deploy</c> clip starts, or put down as it ends
    /// (<paramref name="deployed"/>), with nothing left to play. Null when the model does not load.
    /// </summary>
    public static Node3D? StillModel(PartAssets assets, LiftSettings settings, bool deployed)
    {
        if (assets.InstantiateScene(settings.Model, Identifiers.VanillaModId) is not { } model)
            return null;
        if (FindPlayer(model) is { } player)
        {
            if (player.HasAnimation("deploy") && player.GetAnimation("deploy") is { } deploy)
                PoseAt(model, deploy, deployed ? deploy.Length : 0);
            player.GetParent().RemoveChild(player);
            player.Free();
        }
        return model;
    }

    /// <summary>
    /// Poses <paramref name="model"/> (an instance of the Lift's model) as <paramref name="clip"/> has it at
    /// <paramref name="time"/>, without playing anything. Used for still pictures of the Lift.
    /// </summary>
    public static void PoseAt(Node3D model, Animation clip, double time)
    {
        for (var track = 0; track < clip.GetTrackCount(); track++)
        {
            if (model.GetNodeOrNull<Node3D>(new NodePath(clip.TrackGetPath(track).GetConcatenatedNames())) is { } target)
                ApplyTrack(clip, track, target, clip.TrackGetType(track), time);
        }
    }

    private static void ApplyTrack(Animation clip, int track, Node3D node, Animation.TrackType type, double time)
    {
        switch (type)
        {
            case Animation.TrackType.Position3D:
                node.Position = clip.PositionTrackInterpolate(track, time);
                break;
            case Animation.TrackType.Rotation3D:
                node.Quaternion = clip.RotationTrackInterpolate(track, time);
                break;
            case Animation.TrackType.Scale3D:
                node.Scale = clip.ScaleTrackInterpolate(track, time);
                break;
        }
    }
}
