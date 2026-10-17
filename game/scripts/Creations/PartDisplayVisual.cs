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
/// in while its readout is on. Every part of it is drawn from <see cref="PartReadouts"/>, the same on every peer.
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
            _status.Visible = linked && !connecting && readouts.Has(_creation, Part, _display.Status!);
            StatusRunning = Value(_display.Status) != 0;
            _statusMaterial!.AlbedoColor = StatusRunning ? UiTokens.Success : UiTokens.Disabled;
            _status.GlobalPosition = _part.GlobalPosition + Vector3.Up * (_height / 2 + 0.3f);
        }
        if (_bar is not null)
        {
            BarFill = Math.Clamp(Value(_display.Bar!.Readout), 0f, 1f);
            _bar.Visible = connecting;
            var length = _display.Bar.Length ?? 0.25f;
            _barFill!.Visible = BarFill > 0.001f;
            _barFill.Transform = new Transform3D(Basis.FromScale(new Vector3(1, Math.Max(BarFill, 0.001f), 1)), new Vector3(0, length * BarFill / 2, 0));
        }
        if (_glow is not null)
        {
            var power = Math.Clamp(Value(_display.Glow!.Readout), 0f, 1f);
            _glow.Emitting = power > 0.01f;
            _glow.ScaleAmountMin = 0.3f + 0.5f * power;
            _glow.ScaleAmountMax = 0.5f + 0.7f * power;
            _glow.Color = UiTokens.Cyan with { A = 0.15f + 0.35f * power };
        }
        if (_highlight is not null)
        {
            var on = Value(_display.Highlight!.Readout) != 0;
            _highlight.Visible = on;
            if (on)
                _highlight.RotateY(HighlightSpin * Math.Sign(Value(_display.Highlight.Readout)) * (float)delta);
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
