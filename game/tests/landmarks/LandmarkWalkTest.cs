using Godot;
using Mechanica.Core.Player;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Mechanica.Game.World;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless check that the landmarks' climbs are walkable: each landmark's model (<c>meshes/structures/landmark_*.glb</c>)
/// collides with its <c>collider</c> mesh on a flat floor, and a <see cref="PlayerController"/> walks each route the
/// design promises (stairs, ramps, catwalks, decks) without jumping, from the ground to the top. At each point with a
/// height the player must stand on the floor there. Points are in the model's Blender frame (x, y, z up, as
/// <c>tools/structures/landmarks.py</c> builds it), turned into Godot's (x, z, -y). Exits 0 on success, 1 on failure.
/// </summary>
public partial class LandmarkWalkTest : StagedTest
{
    // How close (m, level) the player must come to a point, how far its feet may be off a point's height, and how
    // many ticks it may go without getting 0.25 m nearer before it counts as stuck.
    private const float Reach = 0.6f;
    private const float HeightSlack = 0.6f;
    private const int StuckTicks = 240;

    private static readonly Dictionary<string, (float X, float Y, float? Z)[][]> Routes = new()
    {
        ["landmark_relay_spire"] = [
            [P(68.00f, -34.00f, 0.00f), P(68.00f, -10.00f, null), P(66.69f, -4.60f, null), P(63.70f, -3.85f, null), P(60.37f, -5.08f, null), P(46.65f, -3.82f, 17.05f), P(34.09f, -2.52f, 25.13f), P(21.52f, -1.22f, 33.21f), P(11.10f, -0.14f, 39.92f)],
        ],
        ["landmark_cooling_towers"] = [
            [P(-25.12f, -20.04f, 0.00f), P(-21.08f, -18.57f, null), P(-17.23f, -17.16f, 3.00f), P(-16.33f, -17.58f, 3.00f), P(-17.03f, -18.58f, null), P(-20.88f, -19.98f, 6.00f), P(-21.78f, -19.56f, 6.00f), P(-21.08f, -18.57f, null), P(-17.23f, -17.16f, 9.00f), P(-16.33f, -17.58f, 9.00f), P(-17.03f, -18.58f, null), P(-20.88f, -19.98f, 12.00f), P(-21.78f, -19.56f, 12.00f), P(-21.88f, -16.37f, null), P(-18.03f, -14.97f, 15.00f), P(-17.13f, -15.39f, 15.00f), P(-17.83f, -16.39f, null), P(-21.68f, -17.79f, 18.00f), P(-22.58f, -17.37f, 18.00f), P(-22.61f, -14.37f, null), P(-18.75f, -12.97f, 21.00f), P(-17.86f, -13.39f, 21.00f), P(-18.56f, -14.39f, null), P(-22.41f, -15.79f, 24.00f), P(-23.31f, -15.37f, 24.00f), P(-23.25f, -12.60f, null), P(-19.40f, -11.20f, 27.00f), P(-18.50f, -11.62f, 27.00f), P(-19.20f, -12.62f, null), P(-23.06f, -14.02f, 30.00f), P(-23.95f, -13.60f, 30.00f), P(-23.70f, -11.36f, null), P(-19.85f, -9.95f, 33.00f), P(-18.95f, -10.37f, 33.00f), P(-19.65f, -11.37f, null), P(-23.51f, -12.77f, 36.00f), P(-24.40f, -12.36f, 36.00f), P(-23.70f, -11.36f, null), P(-19.85f, -9.95f, 39.00f), P(-18.95f, -10.37f, 39.00f), P(-19.65f, -11.37f, null), P(-23.51f, -12.77f, 42.00f), P(-24.40f, -12.36f, 42.00f), P(-23.70f, -11.36f, null), P(-19.85f, -9.95f, 45.00f), P(-18.95f, -10.37f, 45.00f), P(-19.65f, -11.37f, null), P(-23.51f, -12.77f, 48.00f), P(-24.40f, -12.36f, 48.00f), P(-23.70f, -11.36f, null), P(-19.85f, -9.95f, 51.00f), P(-18.95f, -10.37f, 51.00f), P(-19.65f, -11.37f, null), P(-23.51f, -12.77f, 54.00f), P(-24.40f, -12.36f, 54.00f), P(-23.70f, -11.36f, null), P(-19.85f, -9.95f, 57.00f), P(-18.95f, -10.37f, 57.00f), P(-19.65f, -11.37f, null), P(-23.51f, -12.77f, 60.00f), P(-25.19f, -10.20f, 60.00f)],
        ],
        ["landmark_aqueduct"] = [
            [P(-50.50f, -7.30f, 0.00f), P(-54.60f, -7.30f, null), P(-65.00f, -7.30f, 6.75f), P(-66.20f, -8.15f, 6.75f), P(-65.40f, -9.00f, null), P(-55.00f, -9.00f, 13.50f), P(-53.80f, -8.15f, 13.50f), P(-54.60f, -7.30f, null), P(-65.00f, -7.30f, 20.25f), P(-66.20f, -8.15f, 20.25f), P(-65.40f, -9.00f, null), P(-55.00f, -9.00f, 27.00f), P(-52.00f, -8.30f, 27.00f), P(-51.90f, -6.00f, 27.20f), P(-51.90f, -1.00f, 27.20f), P(-20.00f, 0.00f, 27.20f), P(-2.00f, 0.00f, 27.20f)],
        ],
        ["landmark_kneeling_giant"] = [
            [P(0.00f, -31.00f, 0.00f), P(0.00f, -26.50f, null), P(0.00f, -17.50f, 3.10f), P(3.80f, -16.00f, 3.10f), P(5.00f, -14.00f, 3.10f)],
        ],
        ["landmark_sky_arch"] = [
            [P(-74.67f, -2.00f, 0.00f), P(-70.47f, -2.00f, null), P(-63.16f, -2.00f, null), P(-60.56f, -2.00f, 5.89f), P(-51.77f, -2.00f, null), P(-49.17f, -2.00f, 16.94f), P(-39.72f, -2.00f, null), P(-37.12f, -2.00f, 25.29f), P(-27.56f, -2.00f, null), P(-24.96f, -2.00f, 33.78f), P(-16.11f, -2.00f, null), P(-13.51f, -2.00f, 40.84f), P(-8.19f, -2.00f, null), P(-5.59f, -2.00f, 46.01f), P(3.46f, -2.00f, null), P(6.06f, -2.00f, 49.93f), P(11.46f, -1.00f, 49.93f)],
        ],
        ["landmark_ziggurat"] = [
            [P(0.00f, -48.00f, 0.00f), P(0.00f, -43.60f, null), P(0.00f, -9.00f, null), P(0.00f, -7.60f, 27.80f), P(0.00f, -4.00f, 28.10f), P(0.00f, -1.80f, 28.10f)],
            [P(50.00f, -19.00f, 0.00f), P(42.50f, -19.00f, null), P(19.00f, -19.00f, 11.00f), P(12.00f, -19.00f, 11.00f), P(6.50f, -22.50f, 11.00f)],
        ],
        ["landmark_windmill_tower"] = [
            [P(7.50f, -9.60f, 0.00f), P(4.00f, -9.60f, null), P(-4.00f, -9.60f, 5.55f), P(-5.30f, -10.35f, 5.55f), P(-4.00f, -11.10f, null), P(4.00f, -11.10f, 11.10f), P(5.30f, -10.35f, 11.10f), P(4.00f, -9.60f, null), P(-4.00f, -9.60f, 16.65f), P(-5.30f, -10.35f, 16.65f), P(-4.00f, -11.10f, null), P(4.00f, -11.10f, 22.20f), P(5.20f, -9.60f, 22.35f), P(4.60f, -8.20f, 22.35f), P(1.50f, -8.30f, 22.35f), P(-3.00f, -8.20f, 22.35f), P(-5.80f, -5.80f, 22.35f)],
        ],
        ["landmark_freighter_wreck"] = [
            [P(86.00f, 7.00f, 0.00f), P(66.00f, 6.00f, null), P(57.49f, 6.31f, null), P(49.74f, 5.39f, null), P(44.98f, 4.68f, null), P(40.56f, 3.25f, 16.94f), P(38.07f, -1.83f, 17.18f), P(21.47f, -3.57f, 20.43f), P(15.41f, -1.90f, 21.73f), P(25.94f, -1.21f, 25.67f), P(29.21f, -0.94f, 24.72f), P(35.06f, -0.23f, 23.59f)],
            [P(-8.00f, -26.00f, 0.00f), P(-8.00f, -12.00f, null), P(-7.00f, -3.00f, null), P(-2.61f, -3.08f, null), P(1.29f, -2.67f, 6.75f), P(12.03f, -1.54f, 4.65f)],
            [P(-8.00f, -26.00f, 0.00f), P(-8.00f, -12.00f, null), P(-8.00f, 2.00f, null), P(-13.06f, 8.14f, null), P(-17.40f, 7.37f, null), P(-28.00f, 5.51f, 3.68f), P(-38.59f, 3.64f, 1.39f)],
        ],
    };

    private readonly List<string> _failures = [];
    private PlayerController _player = null!;
    private Node3D _site = null!;

    protected override string Tag => "landmarks";

    protected override int StageTimeoutFrames => 60 * 240;

    private static (float, float, float?) P(float x, float y, float? z) => (x, y, z);

    public override void _Ready()
    {
        var floor = new StaticBody3D { Name = "Floor", Position = new Vector3(0, -0.5f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(400, 1, 400) } });
        AddChild(floor);
        _player = new PlayerController { Name = "Player" };
        AddChild(_player);
        foreach (var (id, routes) in Routes)
        {
            Add($"{id}: load", _ => Load(id));
            for (var r = 0; r < routes.Length; r++)
                Walk($"{id}: route {r + 1}", routes[r]);
        }
        // Every route is walked, so one run lists every place that blocks the way.
        Add("every route walked", _ => Check(_failures.Count == 0, $"every route was walked ({_failures.Count} failed)"));
    }

    private void Fail(string what)
    {
        GD.Print($"[{Tag}] FAIL: {what}");
        _failures.Add(what);
    }

    private bool Load(string id)
    {
        _site?.QueueFree();
        var file = Path.Combine(ModPaths.ModsRoot, "vanilla", "meshes", "structures", $"{id}.glb");
        var model = StructureModel.Of(file, id);
        if (!Check(model is not null && model.Colliders.Count > 0, $"{id} loads with a collider"))
            return true;
        _site = new StaticBody3D { Name = id };
        foreach (var (shape, at) in model!.Colliders)
            _site.AddChild(new CollisionShape3D { Shape = shape, Transform = at });
        AddChild(_site);
        return true;
    }

    private static Vector3 Godot((float X, float Y, float? Z) p) => new(p.X, p.Z ?? 0f, -p.Y);

    private void Walk(string name, (float X, float Y, float? Z)[] route)
    {
        var next = 1;
        var best = float.MaxValue;
        var since = 0;
        Add(name, frame =>
        {
            if (frame == 0)
            {
                _player.GlobalPosition = Godot(route[0]) + Vector3.Up * 0.05f;
                _player.Velocity = Vector3.Zero;
                next = 1;
                best = float.MaxValue;
                since = 0;
                return false;
            }
            if (frame < 20)
                return false;
            var at = _player.GlobalPosition;
            var target = Godot(route[next]);
            var to = new Vector2(target.X - at.X, target.Z - at.Z);
            if (to.Length() < Reach)
            {
                if (route[next].Z is { } z && !(_player.IsOnFloor() && Mathf.Abs(at.Y - z) < HeightSlack))
                {
                    Fail($"{name}: reached point {next} but not at its height (y={at.Y:F2}, expected {z:F2})");
                    return true;
                }
                if (++next == route.Length)
                {
                    GD.Print($"[{Tag}] PASS: {name}: walked to the end");
                    _player.CurrentInput = PlayerInput.None;
                    return true;
                }
                best = float.MaxValue;
                since = 0;
                return false;
            }
            if (to.Length() < best - 0.25f)
            {
                best = to.Length();
                since = 0;
            }
            else if (++since > StuckTicks)
            {
                var hits = string.Join(", ", Enumerable.Range(0, _player.GetSlideCollisionCount())
                    .Select(i => _player.GetSlideCollision(i)).Select(c => $"{c.GetPosition()} n{c.GetNormal()}"));
                Fail($"{name}: stuck on the way to point {next} {target} at {at} (on floor: {_player.IsOnFloor()}; touching {hits})");
                _player.CurrentInput = PlayerInput.None;
                return true;
            }
            _player.CurrentInput = new PlayerInput(0, 1, Mathf.Atan2(-to.X, -to.Y), 0, false, false);
            return false;
        });
    }
}
