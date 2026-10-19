using Godot;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Bots;

/// <summary>
/// A bot's animated model (its type's <see cref="BotModel"/>, docs/bot-tutorial.md "Models and animation"). Every peer picks
/// the clip from what it already has: the walk from how fast the bot moves and whether it is engaged, attacks from the
/// host's attack events, <c>hit</c> from a health drop, <c>death</c> from death, and <c>jump</c>, <c>fall</c> and <c>land</c>
/// from its vertical speed. The glowing (emissive) materials turn from the calm colour to the engaged one. Its legs
/// (nodes <c>…_Hip</c>, <c>…_Knee</c>, <c>…_Foot</c>) plant on the ground and the body tilts with it. All of it is
/// cosmetic: the host still moves the bot's capsule.
/// </summary>
public partial class BotModelView : Node3D
{
    // Seconds a clip blends into the next.
    private const float Blend = 0.12f;

    // Below this speed (m/s) a bot stands and idles.
    private const float StandSpeed = 0.15f;

    // Vertical speeds (m/s) that start a jump and count as falling.
    private const float JumpSpeed = 1.5f;
    private const float FallSpeed = -2f;

    // Seconds of falling before the fall clip takes over.
    private const float FallDelay = 0.25f;

    // How far a walking clip may be sped up or slowed down to match the bot.
    private const float MinSpeedScale = 0.4f;
    private const float MaxSpeedScale = 2.5f;

    // Foot placement: how far up and down to look for ground (m), the steepest tilt, the most the body moves up or
    // down (m), how fast it follows (per second), and how far from the camera it runs (m).
    private const float RayUp = 0.6f;
    private const float RayDown = 1.2f;
    private const float MaxTilt = 30f;
    private const float MaxLift = 0.25f;
    private const float PlantRate = 12f;
    private const float PlantDistance = 40f;

    // A foot this high above the model's ground in its clip is in the air (m); planting fades out by twice that.
    private const float StanceHeight = 0.06f;

    private readonly List<StandardMaterial3D> _glows = [];
    private readonly List<Leg> _legs = [];
    private readonly Dictionary<string, int> _attackTurn = [];
    private Node3D _tilt = null!;
    private Node3D _scene = null!;
    private AnimationPlayer? _player;
    private Color _calm;
    private Color _engagedColor;
    private float _eye;
    private float _glowEnergy = 1f;
    private string? _loop;
    private string? _oneShot;
    private double _oneShotLeft;
    private Vector3 _lastPosition;
    private Vector3 _velocity;
    private bool _started;
    private float _airTime;
    private bool _falling;
    private BotView? _view;
    private Quaternion _tiltTarget = Quaternion.Identity;
    private float _liftTarget;

    /// <summary>The bot's type; its <see cref="Bot.Model"/> is the one shown.</summary>
    public Bot Type { get; init; } = null!;

    /// <summary>The loaded model scene (feet at the origin, facing -Z).</summary>
    public Node3D Scene { get; init; } = null!;

    /// <summary>The body to leave out of the ground rays (the bot's own capsule).</summary>
    public Rid OwnerBody { get; init; }

    /// <summary>The clip playing now, for tests.</summary>
    public string? Clip => _oneShot ?? _loop;

    /// <summary>Legs that plant their feet, for tests.</summary>
    public int Legs => _legs.Count;

    /// <summary>How engaged the eye looks, 0 (calm) to 1, for tests.</summary>
    public float EyeEngaged => _eye;

    private BotModel Model => Type.Model!;

    // One leg: its hip turns to put the foot on the ground.
    private sealed class Leg(Node3D hip, Node3D foot)
    {
        public Node3D Hip { get; } = hip;
        public Node3D Foot { get; } = foot;
        public float? Ground { get; set; }
        public float Error { get; set; }
    }

    /// <summary>Loads <paramref name="type"/>'s model, or null when it has none or it does not load.</summary>
    public static BotModelView? For(Bot type, PartAssets? assets, Rid owner)
    {
        if (type.Model is not { } model || assets?.InstantiateScene(model.Mesh, type.ModId) is not { } scene)
            return null;
        return new BotModelView { Name = "Model", Type = type, Scene = scene, OwnerBody = owner };
    }

    public override void _Ready()
    {
        _tilt = new Node3D { Name = "Tilt" };
        AddChild(_tilt);
        _scene = Scene;
        _tilt.AddChild(_scene);
        HexColor.TryParse(Model.EyeCalm, out var calm);
        HexColor.TryParse(Model.EyeEngaged, out var engaged);
        _calm = new Color(calm.R, calm.G, calm.B);
        _engagedColor = new Color(engaged.R, engaged.G, engaged.B);
        CollectGlows(_scene);
        CollectLegs(_scene);
        _player = LiftBody.FindPlayer(_scene);
        if (_player is not null)
        {
            // Advanced by hand, so the feet are planted after the clip has posed them.
            _player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
            foreach (var name in new[] { "idle", "patrol", "chase", "fall" })
            {
                if (_player.HasAnimation(name))
                    _player.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
            }
        }
        SetEye(0f);
    }

    /// <summary>Shows a view from the host: engaging, calming down, being hurt and dying start their clips.</summary>
    public void Show(BotView view)
    {
        var before = _view;
        _view = view;
        if (before is null)
        {
            _eye = view.Engaged ? 1f : 0f;
            if (view.Dead)
                PlayOneShot("death", seek: true);
            return;
        }
        if (view.Dead)
        {
            if (!before.Dead)
                PlayOneShot("death");
            return;
        }
        if (view.Health < before.Health)
            PlayOneShot("hit");
        else if (view.Engaged && !before.Engaged)
            PlayOneShot("alert");
        else if (!view.Engaged && before.Engaged)
            PlayOneShot("lose_interest");
    }

    /// <summary>The bot used <paramref name="attack"/>: plays its clips in turn.</summary>
    public void Attack(string attack)
    {
        if (_view is { Dead: true } || !Model.Attacks.TryGetValue(attack, out var clips))
            return;
        var turn = _attackTurn.GetValueOrDefault(attack);
        _attackTurn[attack] = turn + 1;
        PlayOneShot(clips[turn % clips.Count]);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        Track(dt);
        var dead = _view is { Dead: true };
        SetEye(Mathf.MoveToward(_eye, _view is { Engaged: true } && !dead ? 1f : 0f, dt * 4f));
        if (dead)
            _glowEnergy = Mathf.MoveToward(_glowEnergy, 0f, dt * 0.8f);
        if (_player is not null)
        {
            if (_oneShot is not null && (_oneShotLeft -= delta) <= 0 && !dead)
                _oneShot = null;
            if (_oneShot is null && !dead)
                PlayLoop();
            _player.Advance(delta);
        }
        Plant(dt, dead);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_legs.Count == 0 || !Visible || _view is { Dead: true } || !NearCamera())
        {
            foreach (var leg in _legs)
                leg.Ground = null;
            return;
        }
        var space = GetWorld3D().DirectSpaceState;
        foreach (var leg in _legs)
        {
            var foot = leg.Foot.GlobalPosition;
            var query = PhysicsRayQueryParameters3D.Create(foot + new Vector3(0, RayUp, 0), foot - new Vector3(0, RayDown, 0));
            query.Exclude = [OwnerBody];
            var hit = space.IntersectRay(query);
            // Feet stand on terrain, objects and creations, never on players or other bots.
            leg.Ground = hit.Count > 0 && hit["collider"].AsGodotObject() is not CharacterBody3D ? hit["position"].AsVector3().Y : null;
        }
    }

    // ---- Clips ----

    private void Track(float dt)
    {
        var position = GlobalPosition;
        if (!_started || dt <= 0)
        {
            _started = true;
            _lastPosition = position;
            return;
        }
        var step = (position - _lastPosition) / dt;
        _lastPosition = position;
        _velocity = _velocity.Lerp(step, 1f - Mathf.Exp(-10f * dt));
        if (_view is { Dead: true })
            return;
        var wasFalling = _falling;
        _airTime = _velocity.Y < FallSpeed ? _airTime + dt : 0f;
        _falling = _airTime > FallDelay;
        if (wasFalling && !_falling)
            PlayOneShot("land");
        else if (_velocity.Y > JumpSpeed && _oneShot is not "jump" && !_falling)
            PlayOneShot("jump");
    }

    private void PlayLoop()
    {
        var flat = new Vector2(_velocity.X, _velocity.Z).Length();
        var patrolSpeed = Model.ClipSpeeds.GetValueOrDefault("patrol", 1f);
        var clip = _falling ? "fall"
            : flat < StandSpeed ? "idle"
            : _view is { Engaged: true } || flat > patrolSpeed * 1.6f ? "chase"
            : "patrol";
        if (!_player!.HasAnimation(clip))
            clip = _player.HasAnimation("idle") ? "idle" : null;
        if (clip is null)
            return;
        var scale = Model.ClipSpeeds.TryGetValue(clip, out var speed) ? Math.Clamp(flat / speed, MinSpeedScale, MaxSpeedScale) : 1f;
        if (clip != _loop || !_player.IsPlaying())
            _player.Play(clip, Blend);
        _player.SpeedScale = scale;
        _loop = clip;
    }

    private void PlayOneShot(string clip, bool seek = false)
    {
        if (_player is null || !_player.HasAnimation(clip))
            return;
        _player.SpeedScale = 1f;
        _player.Play(clip, seek ? 0f : Blend);
        _player.Seek(0, true);
        if (seek)
            _player.Seek(_player.GetAnimation(clip).Length, true);
        _oneShot = clip;
        _oneShotLeft = seek ? 0 : _player.GetAnimation(clip).Length;
        _loop = null;
    }

    // ---- Eye ----

    private void CollectGlows(Node node)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
        {
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                if (mesh.SurfaceGetMaterial(i) is StandardMaterial3D { EmissionEnabled: true } material)
                {
                    // Each bot gets its own copy, so its eye changes on its own.
                    var own = (StandardMaterial3D)material.Duplicate();
                    instance.SetSurfaceOverrideMaterial(i, own);
                    _glows.Add(own);
                }
            }
        }
        foreach (var child in node.GetChildren())
            CollectGlows(child);
    }

    private void SetEye(float engaged)
    {
        _eye = engaged;
        var color = _calm.Lerp(_engagedColor, engaged);
        foreach (var material in _glows)
        {
            material.AlbedoColor = color.Darkened(0.4f);
            material.Emission = color;
            material.EmissionEnergyMultiplier = 2.5f * _glowEnergy;
        }
    }

    // ---- Feet ----

    private void CollectLegs(Node node)
    {
        if (node is Node3D hip && node.Name.ToString().EndsWith("_Hip", StringComparison.Ordinal))
        {
            var leg = node.Name.ToString()[..^"_Hip".Length];
            if (hip.FindChild(leg + "_Foot", true, false) is Node3D foot)
                _legs.Add(new Leg(hip, foot));
        }
        foreach (var child in node.GetChildren())
            CollectLegs(child);
    }

    private bool NearCamera() =>
        GetViewport()?.GetCamera3D() is not { } camera || camera.GlobalPosition.DistanceTo(GlobalPosition) < PlantDistance;

    // Tilts the body to the ground under its feet, then turns each standing leg's hip to put its foot on the ground.
    private void Plant(float dt, bool dead)
    {
        var rate = 1f - Mathf.Exp(-PlantRate * dt);
        if (_legs.Count >= 3 && _legs.Count(l => l.Ground is not null) >= 3)
            FitGround();
        else if (!dead)
            (_tiltTarget, _liftTarget) = (Quaternion.Identity, 0f);
        _tilt.Quaternion = _tilt.Quaternion.Slerp(_tiltTarget, rate);
        _tilt.Position = _tilt.Position.Lerp(new Vector3(0, _liftTarget, 0), rate);
        if (dead)
            return;
        foreach (var leg in _legs)
        {
            // How far the clip has the foot off the model's ground: planting is for feet that are down.
            var rest = _tilt.ToLocal(leg.Foot.GlobalPosition).Y;
            var stance = 1f - Math.Clamp((rest - StanceHeight) / StanceHeight, 0f, 1f);
            var error = leg.Ground is { } ground ? (ground - leg.Foot.GlobalPosition.Y) * stance : 0f;
            leg.Error = Mathf.Lerp(leg.Error, error, rate);
            var hip = leg.Hip.GlobalPosition;
            var out_ = leg.Foot.GlobalPosition - hip;
            var reach = new Vector2(out_.X, out_.Z).Length();
            if (reach < 0.05f || Mathf.Abs(leg.Error) < 0.002f)
                continue;
            var flat = new Vector3(out_.X, 0, out_.Z) / reach;
            var axis = flat.Cross(Vector3.Up).Normalized();
            var angle = Mathf.Asin(Math.Clamp(leg.Error / reach, -0.7f, 0.7f));
            leg.Hip.GlobalBasis = new Basis(axis, angle) * leg.Hip.GlobalBasis;
        }
    }

    // A plane through the ground under the feet (least squares, in the bot's frame) sets the body's tilt and height.
    private void FitGround()
    {
        var toLocal = GlobalTransform.AffineInverse();
        double n = 0, sx = 0, sz = 0, sy = 0, sxx = 0, szz = 0, sxz = 0, sxy = 0, szy = 0;
        foreach (var leg in _legs)
        {
            if (leg.Ground is not { } ground)
                continue;
            var foot = leg.Foot.GlobalPosition;
            var p = toLocal * new Vector3(foot.X, ground, foot.Z);
            n++;
            sx += p.X;
            sz += p.Z;
            sy += p.Y;
            sxx += p.X * p.X;
            szz += p.Z * p.Z;
            sxz += p.X * p.Z;
            sxy += p.X * p.Y;
            szy += p.Z * p.Y;
        }
        // Solve [n sx sz; sx sxx sxz; sz sxz szz] [a b c] = [sy sxy szy] for y = a + b x + c z.
        var m = new[,] { { n, sx, sz }, { sx, sxx, sxz }, { sz, sxz, szz } };
        var det = Det(m);
        if (Math.Abs(det) < 1e-9)
            return;
        var a = Det(Replace(m, 0, sy, sxy, szy)) / det;
        var b = Det(Replace(m, 1, sy, sxy, szy)) / det;
        var c = Det(Replace(m, 2, sy, sxy, szy)) / det;
        var limit = Mathf.Tan(Mathf.DegToRad(MaxTilt));
        var normal = new Vector3((float)-Math.Clamp(b, -limit, limit), 1f, (float)-Math.Clamp(c, -limit, limit)).Normalized();
        _tiltTarget = new Quaternion(Vector3.Up, normal);
        _liftTarget = (float)Math.Clamp(a, -MaxLift, MaxLift);
    }

    private static double Det(double[,] m) =>
        m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
        + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

    private static double[,] Replace(double[,] m, int column, double r0, double r1, double r2)
    {
        var copy = (double[,])m.Clone();
        copy[0, column] = r0;
        copy[1, column] = r1;
        copy[2, column] = r2;
        return copy;
    }
}
