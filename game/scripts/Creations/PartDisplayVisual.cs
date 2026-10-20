using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Creations;

/// <summary>
/// What one part shows on itself as its script drives it (UI spec §12, <see cref="DisplayDefinition"/>): a status icon
/// above it (green while its readout is on, gray while off) when it has links and the Connect Tool is put away; a thin
/// cyan bar along it that fills with its readout while the Connect Tool is out; a faint blue particle glow whose
/// strength (size and brightness) follows its readout; a highlight turning about its joint while its readout is not 0; and its model pushed
/// in while its readout is on; the clips of its model that its readouts name, and particle effects (smoke, sparks, drops)
/// whose strength follows a readout. Every part of it is drawn from <see cref="PartReadouts"/>, the same on every peer.
/// </summary>
public sealed class PartDisplayVisual
{
    /// <summary>How fast a highlight turns, in rad/s.</summary>
    public const float HighlightSpin = 2.5f;

    /// <summary>Seconds a press takes to go in or come back out.</summary>
    public const float PressSeconds = 0.08f;

    private static ImageTexture? _dot;
    private readonly int _creation;
    private readonly DisplayDefinition _display;
    private readonly Node3D _part;
    private readonly Node3D _model;
    private readonly float _height;
    private readonly MeshInstance3D? _status;
    private readonly StandardMaterial3D? _statusMaterial;
    private readonly Node3D? _bar;
    private readonly MeshInstance3D? _barFill;
    private readonly CpuParticles3D? _glow;
    private readonly Node3D? _highlight;
    private readonly AnimationPlayer? _player;
    private readonly List<(ScrubDefinition Scrub, AnimationPlayer Player, Animation Clip)> _scrubs = [];
    private readonly List<(EffectDefinition Effect, CpuParticles3D Particles, Color Colour)> _effects = [];
    // What each look was last set from, so a frame that changes nothing touches no node (every display, every frame).
    private readonly List<float> _scrubShown = [];
    private readonly List<float> _effectShown = [];
    private float _glowShown = float.NaN;
    private bool? _statusShown;
    private bool? _statusVisible;
    private bool? _barVisible;
    private bool? _fillVisible;
    private float _fillShown = float.NaN;
    private bool? _highlightVisible;
    private string? _idle;
    private string? _clipValue;
    private float _pressed;

    /// <summary>The part.</summary>
    public int Part { get; }

    /// <summary>The status icon, when the part has one (for tests).</summary>
    public MeshInstance3D? StatusIcon => _status;

    /// <summary>True when the status icon shows the running colour.</summary>
    public bool StatusRunning { get; private set; }

    /// <summary>The bar, when the part has one.</summary>
    public Node3D? Bar => _bar;

    /// <summary>How full the bar is drawn, 0 to 1.</summary>
    public float BarFill { get; private set; }

    /// <summary>The glow's particles, when the part has one.</summary>
    public CpuParticles3D? Glow => _glow;

    /// <summary>The turning highlight, when the part has one.</summary>
    public Node3D? Highlight => _highlight;

    /// <summary>How far in the model is pushed, 0 to 1.</summary>
    public float Pressed => _pressed;

    /// <summary>The clip the model plays now, or null when none was named.</summary>
    public string? Clip { get; private set; }

    /// <summary>The model's player, when the part plays clips (for tests).</summary>
    public AnimationPlayer? Player => _player;

    /// <summary>Each effect's particles, in <see cref="DisplayDefinition.Effects"/> order.</summary>
    public IReadOnlyList<CpuParticles3D> Effects => [.. _effects.Select(e => e.Particles)];

    /// <summary>Builds the visuals under <paramref name="part"/> (the placed part's node) for <paramref name="definition"/>.</summary>
    public PartDisplayVisual(int creation, int partId, PartDefinition definition, Node3D part, Node3D model)
    {
        _creation = creation;
        Part = partId;
        _display = definition.Display!;
        _part = part;
        _model = model;
        _height = (definition.Size ?? GridSize.One).Metres.Y;
        if (_display.Status is not null)
        {
            _statusMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FixedSize = true,
                AlbedoTexture = Dot(),
                AlbedoColor = UiTokens.Disabled,
            };
            _status = new MeshInstance3D
            {
                Name = "StatusIcon", TopLevel = true, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Mesh = new QuadMesh { Size = new Vector2(0.035f, 0.035f) }, MaterialOverride = _statusMaterial,
            };
            part.AddChild(_status);
        }
        if (_display.Bar is { } bar)
        {
            var axis = Direction(bar.Axis, Vector3.Up);
            _bar = new Node3D { Name = "Bar", Visible = false, Transform = new Transform3D(Align(axis), (bar.Offset ?? Vec3.Zero).ToGodot()) };
            var length = bar.Length ?? 0.25f;
            _bar.AddChild(Box("Track", new Vector3(0.025f, length, 0.025f), new Vector3(0, length / 2, 0), UiTokens.From(Core.Ui.UiPalette.DeepNavy, 0.7f)));
            _barFill = Box("Fill", new Vector3(0.03f, length, 0.03f), Vector3.Zero, UiTokens.Cyan);
            _bar.AddChild(_barFill);
            part.AddChild(_bar);
        }
        if (_display.Glow is { } glow)
        {
            var direction = Direction(glow.Direction, Vector3.Up);
            var length = glow.Length ?? 0.5f;
            var colour = UiTokens.Cyan with { A = 0.35f };
            _glow = new CpuParticles3D
            {
                Name = "Glow", Emitting = false, Amount = 32, Lifetime = 0.45, LocalCoords = false,
                Position = (glow.Offset ?? Vec3.Zero).ToGodot(), Direction = direction, Spread = 12,
                InitialVelocityMin = length / 0.45f * 0.7f, InitialVelocityMax = length / 0.45f, Gravity = Vector3.Zero,
                ScaleAmountMin = 0.6f, ScaleAmountMax = 1f, Color = colour,
                Mesh = new QuadMesh
                {
                    Size = new Vector2(0.08f, 0.08f),
                    Material = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                        VertexColorUseAsAlbedo = true, AlbedoTexture = Dot(),
                    },
                },
            };
            part.AddChild(_glow);
        }
        if (_display.Highlight is { } highlight)
        {
            var axis = Direction(highlight.Axis, Vector3.Up);
            var radius = highlight.Radius ?? 0.12f;
            var pivot = new Node3D { Name = "HighlightPivot", Transform = new Transform3D(Align(axis), (highlight.Offset ?? Vec3.Zero).ToGodot()) };
            _highlight = new Node3D { Name = "Highlight", Visible = false };
            // Three short dashes on a ring, so turning shows.
            for (var i = 0; i < 3; i++)
            {
                var angle = i * Mathf.Tau / 3;
                var dash = Box($"Dash{i}", new Vector3(radius * 0.7f, 0.012f, 0.02f), Vector3.Zero, UiTokens.Cyan with { A = 0.6f });
                dash.Transform = new Transform3D(new Basis(Vector3.Up, angle + Mathf.Pi / 2), new Vector3(Mathf.Cos(angle), 0, -Mathf.Sin(angle)) * radius);
                _highlight.AddChild(dash);
            }
            pivot.AddChild(_highlight);
            part.AddChild(pivot);
        }
        if (_display.Clips is { } clips && model.FindChildren("*", nameof(AnimationPlayer), owned: false).OfType<AnimationPlayer>().FirstOrDefault() is { } player)
        {
            _player = player;
            // The clip playing now, kept as it changes rather than read back every frame.
            player.CurrentAnimationChanged += name =>
            {
                if (!string.IsNullOrEmpty(name))
                    Clip = name;
            };
            if (clips.Idle is { } idle && player.HasAnimation(idle))
                _idle = idle;
            foreach (var name in player.GetAnimationList())
            {
                if (name.EndsWith("_loop", StringComparison.Ordinal))
                    player.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
            }
            // Each scrubbed clip gets a player of its own beside the model's, so it holds alongside the played clip.
            foreach (var scrub in clips.Scrub ?? [])
            {
                if (scrub.Clip is not { } name || !player.HasAnimation(name))
                    continue;
                var library = new AnimationLibrary();
                library.AddAnimation(name, player.GetAnimation(name));
                var own = new AnimationPlayer { Name = $"Scrub{_scrubs.Count}", RootNode = player.RootNode };
                own.AddAnimationLibrary("", library);
                player.GetParent().AddChild(own);
                _scrubs.Add((scrub, own, player.GetAnimation(name)));
                _scrubShown.Add(float.NaN);
            }
        }
        foreach (var effect in _display.Effects ?? [])
        {
            var particles = Effect(effect, out var colour);
            part.AddChild(particles);
            _effects.Add((effect, particles, colour));
            _effectShown.Add(float.NaN);
        }
    }

    // The particles of one effect, stopped; Apply starts them and sets their strength.
    private static CpuParticles3D Effect(EffectDefinition effect, out Color colour)
    {
        var kind = effect.Kind ?? EffectKind.Smoke;
        colour = HexColor.TryParse(effect.Color, out var c) ? new Color(c.R, c.G, c.B)
            : kind switch { EffectKind.Sparks => new Color(1f, 0.82f, 0.45f), EffectKind.Drops => new Color(0.06f, 0.05f, 0.04f), _ => new Color(0.17f, 0.16f, 0.15f) };
        var particles = new CpuParticles3D
        {
            Name = $"Effect{kind}", Emitting = false, LocalCoords = false,
            Position = (effect.Offset ?? Vec3.Zero).ToGodot(), Direction = Direction(effect.Direction, Vector3.Up),
            Color = colour,
        };
        var additive = kind == EffectKind.Sparks;
        float size;
        switch (kind)
        {
            case EffectKind.Sparks:
                (particles.Amount, particles.Lifetime, particles.Spread) = (14, 0.35, 55);
                (particles.InitialVelocityMin, particles.InitialVelocityMax, particles.Gravity) = (1f, 2.2f, new Vector3(0, -9.8f, 0));
                size = 0.018f;
                break;
            case EffectKind.Drops:
                (particles.Amount, particles.Lifetime, particles.Spread) = (6, 0.7, 8);
                (particles.InitialVelocityMin, particles.InitialVelocityMax, particles.Gravity) = (0.05f, 0.25f, new Vector3(0, -9.8f, 0));
                size = 0.014f;
                break;
            default:
                (particles.Amount, particles.Lifetime, particles.Spread) = (28, 1.4, 14);
                (particles.InitialVelocityMin, particles.InitialVelocityMax, particles.Gravity) = (0.35f, 0.7f, new Vector3(0, 0.35f, 0));
                particles.DampingMin = particles.DampingMax = 0.4f;
                particles.ScaleAmountCurve = new Curve();
                particles.ScaleAmountCurve.AddPoint(new Vector2(0, 0.35f));
                particles.ScaleAmountCurve.AddPoint(new Vector2(1, 1f));
                size = 0.12f;
                break;
        }
        particles.Mesh = new QuadMesh
        {
            Size = new Vector2(size, size),
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                VertexColorUseAsAlbedo = true, AlbedoTexture = Dot(),
            },
        };
        return particles;
    }

    /// <summary>Shows the part as <paramref name="readouts"/> say, <paramref name="delta"/> seconds after the last time.</summary>
    /// <param name="readouts">What every part shows.</param>
    /// <param name="linked">True when the part has logic links.</param>
    /// <param name="connecting">True while the local player has the Connect Tool out.</param>
    /// <param name="animated">True when the part's model was just turned by its animation (the press adds to it).</param>
    /// <param name="delta">Seconds since the last frame.</param>
    public void Apply(PartReadouts readouts, bool linked, bool connecting, bool animated, double delta)
    {
        float Value(string? id) => id is null ? 0f : readouts.Number(_creation, Part, id) ?? 0f;
        if (_status is not null)
        {
            var visible = linked && !connecting && readouts.Has(_creation, Part, _display.Status!);
            if (_statusVisible != visible)
            {
                _statusVisible = visible;
                _status.Visible = visible;
            }
            StatusRunning = Value(_display.Status) != 0;
            if (_statusShown != StatusRunning)
            {
                _statusShown = StatusRunning;
                _statusMaterial!.AlbedoColor = StatusRunning ? UiTokens.Success : UiTokens.Disabled;
            }
            if (visible)
                _status.GlobalPosition = _part.GlobalPosition + Vector3.Up * (_height / 2 + 0.3f);
        }
        if (_bar is not null)
        {
            BarFill = Math.Clamp(Value(_display.Bar!.Readout), 0f, 1f);
            if (_barVisible != connecting)
            {
                _barVisible = connecting;
                _bar.Visible = connecting;
            }
            if (BarFill != _fillShown)
            {
                _fillShown = BarFill;
                var length = _display.Bar.Length ?? 0.25f;
                if (_fillVisible != BarFill > 0.001f)
                {
                    _fillVisible = BarFill > 0.001f;
                    _barFill!.Visible = BarFill > 0.001f;
                }
                _barFill!.Transform = new Transform3D(Basis.FromScale(new Vector3(1, Math.Max(BarFill, 0.001f), 1)), new Vector3(0, length * BarFill / 2, 0));
            }
        }
        if (_glow is not null)
        {
            var power = Math.Clamp(Value(_display.Glow!.Readout), 0f, 1f);
            if (power != _glowShown)
            {
                _glowShown = power;
                _glow.Emitting = power > 0.01f;
                _glow.ScaleAmountMin = 0.3f + 0.5f * power;
                _glow.ScaleAmountMax = 0.5f + 0.7f * power;
                _glow.Color = UiTokens.Cyan with { A = 0.15f + 0.35f * power };
            }
        }
        if (_highlight is not null)
        {
            var on = Value(_display.Highlight!.Readout) != 0;
            if (_highlightVisible != on)
            {
                _highlightVisible = on;
                _highlight.Visible = on;
            }
            if (on)
                _highlight.RotateY(HighlightSpin * Math.Sign(Value(_display.Highlight.Readout)) * (float)delta);
        }
        if (_player is not null)
        {
            var value = readouts.Text(_creation, Part, _display.Clips!.Readout!);
            if (value != _clipValue)
            {
                _clipValue = value;
                // "start>run_loop#3": play start, then run_loop; what follows the # only makes the value new.
                var names = value?.Split('#')[0].Split('>') ?? [];
                if (names.Length > 0 && _player.HasAnimation(names[0]))
                {
                    Clip = names[0];
                    _player.Stop();
                    _player.Play(names[0]);
                    foreach (var next in names.Skip(1).Where(n => _player.HasAnimation(n)))
                        _player.Queue(next);
                }
            }
            if (_idle is { } idle && !_player.IsPlaying())
                _player.Play(idle);
            for (var i = 0; i < _scrubs.Count; i++)
            {
                var (scrub, player, clip) = _scrubs[i];
                var at = Math.Clamp(Value(scrub.Readout), 0f, 1f) * (float)clip.Length;
                // Only this display plays the scrub's own player, so its clip stays put once set.
                if (at == _scrubShown[i])
                    continue;
                _scrubShown[i] = at;
                if (player.CurrentAnimation != scrub.Clip!)
                    player.Play(scrub.Clip!);
                player.Pause();
                player.Seek(at, update: true);
            }
        }
        for (var i = 0; i < _effects.Count; i++)
        {
            var (effect, particles, colour) = _effects[i];
            var power = Math.Clamp(Value(effect.Readout), 0f, 1f);
            if (power == _effectShown[i])
                continue;
            _effectShown[i] = power;
            particles.Emitting = power > 0.01f;
            particles.SpeedScale = 0.6 + 0.6 * power;
            particles.ScaleAmountMin = 0.5f + 0.4f * power;
            particles.ScaleAmountMax = 0.7f + 0.6f * power;
            particles.Color = colour with { A = effect.Kind == EffectKind.Sparks ? 1f : 0.3f + 0.5f * power };
        }
        if (_display.Press is { } press)
        {
            var target = Value(press.Readout) != 0 ? 1f : 0f;
            _pressed = Mathf.MoveToward(_pressed, target, (float)delta / PressSeconds);
            var push = Direction(press.Axis, Vector3.Down) * (press.Depth ?? 0.02f) * _pressed;
            _model.Position = (animated ? _model.Position : Vector3.Zero) + push;
        }
    }

    private static Vector3 Direction(Vec3? v, Vector3 fallback) => v is { } d ? d.ToGodot().Normalized() : fallback;

    // A basis turning +Y onto axis.
    private static Basis Align(Vector3 axis) =>
        axis.Y < -0.99f ? new Basis(Vector3.Right, Mathf.Pi) : new Basis(new Quaternion(Vector3.Up, axis));

    private static MeshInstance3D Box(string name, Vector3 size, Vector3 at, Color colour) => new()
    {
        Name = name,
        Position = at,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Mesh = new BoxMesh { Size = size },
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = colour.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            AlbedoColor = colour,
        },
    };

    // A soft round dot, white, for the icon and the glow.
    private static ImageTexture Dot()
    {
        if (_dot is not null)
            return _dot;
        const int size = 32;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).Length() / (size / 2f);
                var a = Mathf.Clamp((1f - d) * 4f, 0f, 1f);
                image.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        }
        return _dot = ImageTexture.CreateFromImage(image);
    }
}
