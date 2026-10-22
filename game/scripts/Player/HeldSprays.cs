using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Game.Net;

namespace Mechanica.Game.Player;

/// <summary>
/// Sprays from held items, as every peer sees them (engine item C8): while a player holds Use with an item whose
/// <c>held.spray</c> is set (the Fire Extinguisher's foam), a cone of particles leaves its held model's <c>nozzle</c> node
/// (or the model's origin) along where the player looks, in the first-person arms and on the body alike. It reads only the
/// held-item number and the guard flag the player already sends (<see cref="Building.ItemTool.Hold"/>), so it costs no
/// traffic; what the spray does is the item's script, on the host.
/// </summary>
public partial class HeldSprays : Node
{
    /// <summary>The node in a held model the spray leaves from.</summary>
    public const string NozzleNode = "nozzle";

    private const float DefaultLength = 4f;
    private const float DefaultAngle = 15f;
    private const float DefaultSize = 0.08f;
    // Seconds a particle flies; its speed is the spray's length over that.
    private const float Lifetime = 0.55f;

    private readonly Dictionary<CharacterModel, Spray> _sprays = [];

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The spray of the held item numbered as players send it (<see cref="HeldItems"/>), or null for none.</summary>
    public Func<ushort, HeldSprayDefinition?> SprayOf { get; init; } = _ => null;

    /// <summary>True while <paramref name="peer"/>'s held item sprays in the model drawn for them now.</summary>
    public bool IsSpraying(int peer) =>
        Players.Players.TryGetValue(peer, out var player) && Shown(player) is { } model && _sprays.TryGetValue(model, out var spray) && spray.Particles.Emitting;

    /// <summary>The particles spraying from <paramref name="peer"/>'s held item in the model drawn for them now, or null.</summary>
    public CpuParticles3D? ParticlesOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) && Shown(player) is { } model && _sprays.TryGetValue(model, out var spray) ? spray.Particles : null;

    public override void _Process(double delta)
    {
        var seen = new HashSet<CharacterModel>();
        foreach (var player in Players.Players.Values)
        {
            var definition = player.ShownHeld != 0 ? SprayOf(player.ShownHeld) : null;
            foreach (var model in new[] { player.Character as CharacterModel, player.Arms as CharacterModel })
            {
                if (model is null || !IsInstanceValid(model) || definition is null || model.HeldNode is not { } held || !IsInstanceValid(held))
                    continue;
                if (!_sprays.TryGetValue(model, out var spray) || spray.Held != held || !IsInstanceValid(spray.Particles))
                {
                    spray?.Particles.QueueFree();
                    _sprays[model] = spray = Make(held, definition);
                }
                seen.Add(model);
                var on = player.Pose.Guard && model.Visible && model.IsVisibleInTree();
                var particles = spray.Particles;
                if (on)
                {
                    // In world space, so the foam stays where it was sprayed as the player turns.
                    particles.GlobalPosition = spray.Nozzle.GlobalPosition;
                    var look = -player.Head.GlobalBasis.Z.Normalized();
                    particles.Direction = look;
                }
                if (particles.Emitting != on)
                    particles.Emitting = on;
            }
        }
        foreach (var model in _sprays.Keys.Where(m => !seen.Contains(m)).ToList())
        {
            if (IsInstanceValid(_sprays[model].Particles))
                _sprays[model].Particles.QueueFree();
            _sprays.Remove(model);
        }
    }

    // The model whose hand shows: the first-person arms while the player looks out of its own eyes, else the body.
    private static CharacterModel? Shown(PlayerController player) =>
        player.Arms is CharacterModel { Visible: true } arms && arms.HeldNode is not null ? arms : player.Character as CharacterModel;

    // A cone of soft round puffs that slow down and spread as they go: foam, not a jet.
    private Spray Make(Node3D held, HeldSprayDefinition definition)
    {
        var colour = definition.Color is { } hex && Color.HtmlIsValid(hex) ? Color.FromHtml(hex) : Colors.White;
        var length = definition.Length ?? DefaultLength;
        var size = definition.Size ?? DefaultSize;
        var speed = length / Lifetime * 1.6f; // damping takes about a third of it off before it lands
        var particles = new CpuParticles3D
        {
            Name = "HeldSpray",
            Emitting = false,
            Amount = 200,
            Lifetime = Lifetime,
            LocalCoords = false,
            Direction = Vector3.Forward,
            Spread = definition.Angle ?? DefaultAngle,
            InitialVelocityMin = speed * 0.8f,
            InitialVelocityMax = speed,
            DampingMin = speed * 0.9f,
            DampingMax = speed * 1.3f,
            Gravity = new Vector3(0f, -2.5f, 0f),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1f,
            Color = colour,
            Mesh = new SphereMesh
            {
                Radius = size / 2,
                Height = size,
                RadialSegments = 8,
                Rings = 4,
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    VertexColorUseAsAlbedo = true,
                    Roughness = 0.9f,
                },
            },
        };
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 0.25f));
        curve.AddPoint(new Vector2(0.4f, 1f));
        curve.AddPoint(new Vector2(1f, 1.6f));
        curve.MaxValue = 2f;
        particles.ScaleAmountCurve = curve;
        var fade = new Gradient();
        fade.SetColor(0, colour with { A = 0.95f });
        fade.SetColor(1, colour with { A = 0f });
        particles.ColorRamp = fade;
        // Not under the hand: the spray must not inherit its scale and turns. The scene root keeps it in the world.
        AddChild(particles);
        var nozzle = held.FindChild(NozzleNode, recursive: true, owned: false) as Node3D ?? held;
        return new Spray(held, nozzle, particles);
    }

    private sealed record Spray(Node3D Held, Node3D Nozzle, CpuParticles3D Particles);
}
