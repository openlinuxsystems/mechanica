using Godot;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Bots;

/// <summary>
/// The rope of every Lead in use, on every peer: from the hand of the player leading an animal (<c>LeadBot</c>) to its
/// model's <c>lead_node</c> (a collar's ring), sagging when slack and pulled straight as the animal falls behind. Drawn
/// from what every peer already knows (the bot's <see cref="Core.Bots.BotView.LedBy"/> and the players' rigs), so it
/// costs no traffic.
/// </summary>
public partial class BotLeads : Node3D
{
    private const int Segments = 18;
    private const int Sides = 6;
    private const float Radius = 0.011f;

    // The Lead's twisted rope (tools/husbandry/build_lead.py: ROPE).
    private static readonly Color Rope = new("#c8a46a");

    private readonly Dictionary<int, MeshInstance3D> _ropes = [];
    private StandardMaterial3D _material = null!;

    /// <summary>The bots. Set before adding to the tree.</summary>
    public BotSync Bots { get; init; } = null!;

    /// <summary>The players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Bots whose rope is drawn now (tests read it).</summary>
    public IEnumerable<int> Drawn => _ropes.Where(r => r.Value.Visible).Select(r => r.Key);

    /// <summary>Most rope out before it pulls straight, m (the bots' <c>lead_length</c> plus a little).</summary>
    public float Slack { get; set; } = 3f;

    public override void _Ready()
    {
        TopLevel = true;
        ProcessPriority = 100; // after the players' and bots' poses this frame
        _material = new StandardMaterial3D { AlbedoColor = Rope, Roughness = 0.85f };
    }

    public override void _Process(double delta)
    {
        foreach (var gone in _ropes.Keys.Where(id => !Bots.Bodies.ContainsKey(id)).ToList())
        {
            _ropes[gone].QueueFree();
            _ropes.Remove(gone);
        }
        foreach (var (id, body) in Bots.Bodies)
        {
            var ends = body.View is { LedBy: { } peer, Dead: false } && Players.Players.TryGetValue(peer, out var player) && Hand(player) is { } hand
                ? (Hand: hand, Ring: Ring(body)) : ((Vector3 Hand, Vector3 Ring)?)null;
            if (ends is null)
            {
                if (_ropes.TryGetValue(id, out var idle))
                    idle.Visible = false;
                continue;
            }
            if (!_ropes.TryGetValue(id, out var rope))
            {
                rope = _ropes[id] = new MeshInstance3D { Name = $"Lead{id}", Mesh = new ImmediateMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(rope);
            }
            rope.Visible = true;
            Draw((ImmediateMesh)rope.Mesh, ends.Value.Hand, ends.Value.Ring);
        }
    }

    // The hand that shows: the first-person arms when they are drawn, otherwise the body.
    private static Vector3? Hand(PlayerController player)
    {
        var model = !player.ThirdPerson && player.Arms is CharacterModel { Visible: true } arms ? arms : player.Character as CharacterModel;
        return model?.HandPosition;
    }

    private static Vector3 Ring(BotBody body) =>
        body.Model?.LeadNode is { } node && node.IsInsideTree() ? node.GlobalPosition : body.GlobalPosition + new Vector3(0, body.Type.Height * 0.6f, 0);

    // A tube along a parabola that sags by the slack left in the rope.
    private void Draw(ImmediateMesh mesh, Vector3 from, Vector3 to)
    {
        var span = from.DistanceTo(to);
        var sag = Mathf.Sqrt(Mathf.Max(0f, Slack * Slack - span * span)) * 0.35f;
        var points = new Vector3[Segments + 1];
        for (var i = 0; i <= Segments; i++)
        {
            var t = (float)i / Segments;
            points[i] = from.Lerp(to, t) - new Vector3(0, sag * 4 * t * (1 - t), 0);
        }
        mesh.ClearSurfaces();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _material);
        var up = Vector3.Up;
        for (var i = 0; i < Segments; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            var along = (b - a).Normalized();
            var side = (Mathf.Abs(along.Dot(up)) > 0.95f ? Vector3.Right : up).Cross(along).Normalized();
            var other = along.Cross(side);
            for (var k = 0; k < Sides; k++)
            {
                var a0 = Mathf.Tau * k / Sides;
                var a1 = Mathf.Tau * (k + 1) / Sides;
                var n0 = side * Mathf.Cos(a0) + other * Mathf.Sin(a0);
                var n1 = side * Mathf.Cos(a1) + other * Mathf.Sin(a1);
                Quad(mesh, a + n0 * Radius, a + n1 * Radius, b + n1 * Radius, b + n0 * Radius, n0, n1);
            }
        }
        mesh.SurfaceEnd();
    }

    private static void Quad(ImmediateMesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb)
    {
        foreach (var (p, n) in new[] { (a, na), (c, nb), (b, nb), (a, na), (d, na), (c, nb) })
        {
            mesh.SurfaceSetNormal(n);
            mesh.SurfaceAddVertex(p);
        }
    }
}
