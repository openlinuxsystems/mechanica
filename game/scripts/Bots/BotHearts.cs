using Godot;

namespace Mechanica.Game.Bots;

/// <summary>
/// Hearts that rise from an animal and fade, on every peer: a few when a player feeds it, more when it is tamed. It
/// frees itself when they have gone.
/// </summary>
public partial class BotHearts : Node3D
{
    private const float Seconds = 1.8f;
    private const float Rise = 0.9f;
    private const float Size = 0.22f;

    private static readonly Color Pink = new(0.95f, 0.36f, 0.5f);
    private static ImageTexture? _heart;

    private readonly List<(MeshInstance3D Quad, StandardMaterial3D Material, Vector3 From, float Delay)> _hearts = [];
    private float _age;

    /// <summary>How many hearts rise.</summary>
    public int Count { get; init; } = 3;

    /// <summary>Puts hearts above <paramref name="at"/> (a point over the animal's back) under <paramref name="parent"/>.</summary>
    public static BotHearts Show(Node parent, Vector3 at, int count)
    {
        var hearts = new BotHearts { Name = "BotHearts", Count = count };
        parent.AddChild(hearts);
        hearts.GlobalPosition = at;
        return hearts;
    }

    public override void _Ready()
    {
        var random = new RandomNumberGenerator();
        for (var i = 0; i < Count; i++)
        {
            var material = new StandardMaterial3D
            {
                AlbedoTexture = Heart(),
                AlbedoColor = Pink,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = false,
            };
            var quad = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(Size, Size), Material = material }, Visible = false };
            AddChild(quad);
            var from = new Vector3(random.RandfRange(-0.3f, 0.3f), random.RandfRange(0f, 0.15f), random.RandfRange(-0.3f, 0.3f));
            _hearts.Add((quad, material, from, i * 0.18f));
        }
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        var done = true;
        foreach (var (quad, material, from, delay) in _hearts)
        {
            var t = (_age - delay) / Seconds;
            quad.Visible = t is > 0 and < 1;
            if (t < 1)
                done = false;
            if (!quad.Visible)
                continue;
            quad.Position = from + new Vector3(Mathf.Sin(t * Mathf.Tau) * 0.06f, t * Rise, 0);
            quad.Scale = Vector3.One * Mathf.Min(1f, t * 6f);
            material.AlbedoColor = Pink with { A = 1f - Mathf.Pow(t, 2f) };
        }
        if (done)
            QueueFree();
    }

    // A heart drawn once: inside where (x² + y² - 1)³ - x² y³ < 0, with a soft edge.
    private static ImageTexture Heart()
    {
        if (_heart is not null)
            return _heart;
        const int n = 64;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var py = 0; py < n; py++)
        {
            for (var px = 0; px < n; px++)
            {
                var x = (px + 0.5f) / n * 2.6f - 1.3f;
                var y = 1.25f - (py + 0.5f) / n * 2.6f;
                var f = Mathf.Pow(x * x + y * y - 1f, 3f) - x * x * y * y * y;
                var alpha = Mathf.Clamp(-f * 12f, 0f, 1f);
                image.SetPixel(px, py, new Color(1, 1, 1, alpha));
            }
        }
        return _heart = ImageTexture.CreateFromImage(image);
    }
}
