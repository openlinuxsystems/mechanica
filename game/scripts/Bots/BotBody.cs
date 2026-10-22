using Godot;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Game.Creations;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Bots;

/// <summary>
/// A bot in the scene: a capsule body showing its type's animated model (<see cref="BotModelView"/>), or a placeholder
/// look for a type without one (its colour and a dark visor showing which way it faces), and a health label once it is
/// hurt. On the host it walks by <see cref="CharacterBody3D.MoveAndSlide"/>; on clients it follows the host's snapshots.
/// </summary>
public partial class BotBody : CharacterBody3D
{
    // How quickly a client's body catches up with the last snapshot (per second).
    private const float FollowRate = 12f;

    private Label3D _label = null!;
    private MeshInstance3D? _mesh;
    private StandardMaterial3D? _material;
    private Vector3 _target;
    private float _targetYaw;
    private bool _placed;
    private float _flash;
    private float _cueTime;

    /// <summary>The bot's id.</summary>
    public int BotId { get; init; }

    /// <summary>Its type.</summary>
    public Bot Type { get; init; } = null!;

    /// <summary>Which of its model's looks it has, or null to go by its id (<see cref="BotView.Look"/>).</summary>
    public int? Look { get; init; }

    /// <summary>Where its model loads from; null shows the placeholder.</summary>
    public PartAssets? Assets { get; init; }

    /// <summary>Plays its model's idle sounds; null keeps it quiet.</summary>
    public Sound.SoundSync? Sounds { get; init; }

    /// <summary>The picture of what it gives when tame (<c>tame.produce</c>: the Grazer's milk), shown over it when it is ready; null for none.</summary>
    public Texture2D? ProduceIcon { get; init; }

    /// <summary>
    /// The cue over a tame animal whose produce is ready to collect (<see cref="BotView.Ready"/>): its item's picture on a
    /// round bone badge, bobbing over its back. Shown on every peer.
    /// </summary>
    public Node3D ProduceCue { get; } = new() { Name = "ProduceCue", Visible = false };

    /// <summary>Its animated model, or null when it shows the placeholder.</summary>
    public BotModelView? Model { get; private set; }

    /// <summary>The view last shown.</summary>
    public BotView? View { get; private set; }

    /// <summary>Vertical speed while falling (host), m/s.</summary>
    public float Fall { get; set; }

    public override void _Ready()
    {
        var shape = new CapsuleShape3D { Radius = Type.Radius, Height = Type.Height };
        AddChild(new CollisionShape3D { Shape = shape, Position = new Vector3(0, Type.Height / 2, 0) });
        Model = BotModelView.For(Type, Assets, GetRid(), BotId, Look);
        if (Model is not null)
            Model.PlaySound = sound => Sounds?.PlayLocal(sound, Type.ModId, GlobalPosition + new Vector3(0, Type.Height / 2, 0));
        if (Model is not null)
            AddChild(Model);
        else
            AddPlaceholder();
        _label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, Type.Height + 0.35f, 0),
            FontSize = 48,
            PixelSize = 0.005f,
            Visible = false,
        };
        AddChild(Loc.Verbatim(_label)); // names are the mods' own text
        AddProduceCue();
        FloorSnapLength = 0.3f;
    }

    private static ImageTexture? _disc;

    // A round badge, its edge smoothed over a pixel or two.
    private static ImageTexture Disc()
    {
        if (_disc is not null)
            return _disc;
        const int n = 64;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).Length();
                image.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(n / 2f - 1f - d, 0f, 1f)));
            }
        }
        return _disc = ImageTexture.CreateFromImage(image);
    }

    private void AddProduceCue()
    {
        ProduceCue.Position = new Vector3(0, Type.Height + 0.45f, 0);
        AddChild(ProduceCue);
        var badge = new StandardMaterial3D
        {
            AlbedoTexture = Disc(),
            AlbedoColor = UiTokens.Text with { A = 0.92f },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        ProduceCue.AddChild(new MeshInstance3D
        {
            Name = "Badge",
            Mesh = new QuadMesh { Size = new Vector2(0.42f, 0.42f), Material = badge },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        ProduceCue.AddChild(new Sprite3D
        {
            Name = "Icon",
            Texture = ProduceIcon,
            PixelSize = ProduceIcon is null ? 0.01f : 0.34f / Mathf.Max(1, ProduceIcon.GetWidth()),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            Position = new Vector3(0, 0, 0.01f),
            RenderPriority = 1,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private void AddPlaceholder()
    {
        HexColor.TryParse(Type.Color, out var c);
        _material = new StandardMaterial3D { AlbedoColor = new Color(c.R, c.G, c.B) };
        _mesh = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = Type.Radius, Height = Type.Height, Material = _material },
            Position = new Vector3(0, Type.Height / 2, 0),
        };
        AddChild(_mesh);
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(Type.Radius * 1.2f, Type.Height * 0.12f, Type.Radius * 0.5f),
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.1f) },
            },
            Position = new Vector3(0, Type.Height * 0.78f, -Type.Radius * 0.8f),
        });
    }

    /// <summary>Shows a view from the host: health, death, and (on clients) where to move to.</summary>
    public void Show(BotView view, bool follow)
    {
        // Hearts rise when a player feeds it, more when that tames it or puts it in the mood to pair, and when its produce is collected.
        if (View is { } before && !view.Dead && GetParent() is { } world)
        {
            if (view.Kept && !before.Kept)
                BotHearts.Show(world, GlobalPosition + new Vector3(0, Type.Height, 0), 7);
            else if (view.InLove && !before.InLove)
                BotHearts.Show(world, GlobalPosition + new Vector3(0, Type.Height, 0), 5);
            else if (view.Fed > before.Fed)
                BotHearts.Show(world, GlobalPosition + new Vector3(0, Type.Height, 0), 2);
            else if (before.Ready && !view.Ready)
                BotHearts.Show(world, GlobalPosition + new Vector3(0, Type.Height, 0), 3); // its produce was collected
        }
        View = view;
        ProduceCue.Visible = view.Ready && !view.Dead;
        _label.Visible = view.Health < view.MaxHealth;
        _label.Text = view.Dead ? Loc.T("{0} (destroyed)", Type.Name) : $"{Type.Name}  {view.Health:0}/{view.MaxHealth:0}";
        if (_material is not null && view.Dead)
            _material.AlbedoColor = new Color(0.2f, 0.2f, 0.2f);
        Model?.Show(view);
        // A dead bot's body no longer blocks shots or players.
        CollisionLayer = view.Dead ? 0u : 1u;
        if (follow)
            Follow(view.Position.ToGodot(), view.Yaw);
    }

    /// <summary>Clients: a snapshot of where the bot is.</summary>
    public void Follow(Vector3 position, float yaw)
    {
        _target = position;
        _targetYaw = Mathf.DegToRad(yaw);
        if (_placed)
            return;
        _placed = true;
        GlobalPosition = position;
        Rotation = new Vector3(0, _targetYaw, 0);
    }

    /// <summary>
    /// The bot used <paramref name="attack"/>, hitting <paramref name="hits"/> (world points): its model plays the
    /// attack's clip, the placeholder flashes.
    /// </summary>
    public void Attacked(string attack, IReadOnlyList<Vector3> hits)
    {
        if (Model is not null)
            Model.Attack(attack, hits);
        else
            _flash = 0.25f;
    }

    /// <summary>Clients: moves toward the last snapshot.</summary>
    public void Interpolate(float delta)
    {
        var t = 1f - Mathf.Exp(-FollowRate * delta);
        GlobalPosition = GlobalPosition.Lerp(_target, t);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, _targetYaw, t), 0);
    }

    public override void _Process(double delta)
    {
        if (ProduceCue.Visible)
        {
            // a gentle bob and pulse, so it catches the eye
            _cueTime += (float)delta;
            ProduceCue.Position = new Vector3(0, Type.Height + 0.45f + 0.06f * Mathf.Sin(_cueTime * 2.4f), 0);
            ProduceCue.Scale = Vector3.One * (1f + 0.06f * Mathf.Sin(_cueTime * 4.8f));
        }
        if (_flash <= 0 || _mesh is null)
            return;
        _flash -= (float)delta;
        _mesh.Scale = Vector3.One * (1f + Mathf.Max(0, _flash) * 0.6f);
    }
}
