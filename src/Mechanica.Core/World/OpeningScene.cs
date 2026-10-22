using System.Numerics;
using Mechanica.Core.Creations;

namespace Mechanica.Core.World;

/// <summary>Where the camera is, what it looks at and how wide it sees.</summary>
/// <param name="Position">The camera.</param>
/// <param name="Target">The point it looks at.</param>
/// <param name="Fov">Vertical field of view in degrees.</param>
public readonly record struct OpeningCamera(Vector3 Position, Vector3 Target, float Fov);

/// <summary>Where the ship and its pieces are at one moment.</summary>
/// <param name="Ship">The flying ship's origin (the intact ship's frame: nose towards -z).</param>
/// <param name="Pieces">Each wreck piece's model origin, in <see cref="OpeningSettings.Pieces"/>' order.</param>
/// <param name="Housing">The engine housing model's origin.</param>
/// <param name="Containers">Each falling container model's origin, in <see cref="OpeningScene.ContainerIndices"/>' order.</param>
public readonly record struct OpeningPieces(Pose Ship, IReadOnlyList<Pose> Pieces, Pose Housing, IReadOnlyList<Pose> Containers);

/// <summary>
/// Samples the opening cinematic (<see cref="OpeningSettings"/>) for one placed crash site: the ship's flight, its pieces,
/// the engine housing and the containers (copies of the wreck's own models, which end exactly where the wreck's models
/// lie) and the camera. World space, metres, as the POI and its models are placed.
/// </summary>
public sealed class OpeningScene
{
    private readonly OpeningSettings _settings;
    private readonly PoiInstance _poi;
    private readonly List<(float T, Pose Pose)> _ship;
    private readonly Pose[] _piecesIntact;
    private readonly Pose _housingOnShip;
    private readonly Pose[] _containersOnShip;
    private readonly float[] _release;
    private readonly Func<float, float, float>? _ground;
    private readonly float[] _lift = [];

    /// <summary>
    /// The scene for <paramref name="poi"/>, whose prefab must place the models <paramref name="settings"/> names (and a
    /// slot for each container model it places); their placements are where the pieces end. <paramref name="ground"/>
    /// gives the ground's height at a world x and z: the ship rises over hills in its way and cameras stay above the ground.
    /// </summary>
    public OpeningScene(OpeningSettings settings, PoiInstance poi, Func<float, float, float>? ground = null)
    {
        _ground = ground;
        _settings = settings;
        _poi = poi;
        var models = (poi.Poi.Prefab.Models ?? []).ToList();
        int Find(string file) => models.FindIndex(m => m.File == file) is var i and >= 0 ? i
            : throw new InvalidOperationException($"POI '{poi.Poi.QualifiedId}' places no model '{file}'.");
        PieceIndices = [.. settings.Pieces.Select(p => Find(p.Model))];
        HousingIndex = Find(settings.HousingModel);
        ContainerIndices = [.. models.Select((m, i) => (m, i)).Where(p => settings.Containers.Models.Contains(p.m.File ?? "")).Select(p => p.i)];
        if (ContainerIndices.Count != settings.Containers.Slots.Count)
            throw new InvalidOperationException($"POI '{poi.Poi.QualifiedId}' places {ContainerIndices.Count} containers for {settings.Containers.Slots.Count} slots.");
        PieceWrecks = [.. PieceIndices.Select(i => Placed(models[i]))];
        Wreck = PieceWrecks[0];
        HousingWreck = Placed(models[HousingIndex]);
        ContainerWrecks = [.. ContainerIndices.Select(i => Placed(models[i]))];
        HiddenModels = [.. PieceIndices, HousingIndex, .. ContainerIndices,
            .. models.Select((m, i) => (m, i)).Where(p => settings.HiddenModels.Contains(p.m.File ?? "")).Select(p => p.i)];
        // The pieces' models are baked as the wreck lies: each turned about its pivot and set down. The intact ship undoes it.
        _piecesIntact = [.. settings.Pieces.Select(p => new Pose(FromBlender(p.Pivot), Quaternion.Identity).Compose(Blender(p.Wreck).Inverse()))];
        _housingOnShip = new Pose(FromBlender(settings.HousingOnShip), Quaternion.Identity).Compose(Blender(settings.HousingLying).Inverse());
        _containersOnShip = [.. settings.Containers.Slots.Select(s => new Pose(FromBlender(s) - new Vector3(0, settings.Containers.Middle, 0), Quaternion.Identity))];
        _ship = [.. settings.Ship.Select(k => (k.T, KeyPose(k))), (settings.Settle, Wreck)];
        _lift = Lift();
        _release = [.. ContainerWrecks.Select(Release)];
    }

    /// <summary>Metres the ship flies above the ground at least, until the housing comes down; then less, to nothing at touchdown.</summary>
    public const float Clearance = 12f;

    // Seconds between lift samples, and how many seconds a lift ramps in and out over.
    private const float LiftStep = 0.25f;
    private const float LiftRamp = 3f;

    // How far the keyed path has to rise at each sample to clear the ground under the ship and beside it, eased in and
    // out so the ship climbs over a hill instead of jumping.
    private float[] Lift()
    {
        if (_ground is null)
            return [];
        var count = (int)(_settings.Settle / LiftStep) + 2;
        var need = new float[count];
        for (var k = 0; k < count; k++)
        {
            var t = k * LiftStep;
            var clearance = t <= _settings.HousingDown ? Clearance
                : Clearance * Math.Clamp((_settings.Touchdown - t) / (_settings.Touchdown - _settings.HousingDown), 0f, 1f);
            var ship = Keyed(t).Position;
            var ground = new[] { (0f, 0f), (10f, 0f), (-10f, 0f), (0f, 10f), (0f, -10f) }.Max(o => _ground(ship.X + o.Item1, ship.Z + o.Item2));
            need[k] = Math.Max(0f, ground + clearance - ship.Y);
        }
        var reach = (int)(LiftRamp / LiftStep);
        var lift = new float[count];
        for (var k = 0; k < count; k++)
            for (var j = Math.Max(0, k - reach); j < Math.Min(count, k + reach + 1); j++)
                lift[k] = Math.Max(lift[k], need[j] * (1f - Math.Abs(j - k) / (float)(reach + 1)));
        return lift;
    }

    // When a container breaks away: as the ship passes closest over where it lands (allowing for the way it carries on
    // forward as it falls), between the explosion and the last moment it can still land before touchdown.
    private float Release(Pose landing)
    {
        var fall = _settings.Containers.Fall;
        var best = (T: _settings.Explode, Miss: float.MaxValue);
        for (var t = _settings.Explode; t <= _settings.Touchdown - fall + 1e-3f; t += 0.05f)
        {
            var drop = Ship(t).Position + ShipVelocity(t) * fall * 0.4f;
            var miss = Vector2.Distance(new Vector2(drop.X, drop.Z), new Vector2(landing.Position.X, landing.Position.Z));
            if (miss < best.Miss)
                best = (t, miss);
        }
        return best.T;
    }

    /// <summary>The pieces' indices in the POI's models, in <see cref="OpeningSettings.Pieces"/>' order.</summary>
    public IReadOnlyList<int> PieceIndices { get; }

    /// <summary>The engine housing's index in the POI's models.</summary>
    public int HousingIndex { get; }

    /// <summary>The falling containers' indices in the POI's models, in the prefab's order.</summary>
    public IReadOnlyList<int> ContainerIndices { get; }

    /// <summary>
    /// The POI's models kept out of sight until <see cref="OpeningSettings.Settle"/>: the pieces, the housing and the
    /// containers (their copies fly instead) and <see cref="OpeningSettings.HiddenModels"/>.
    /// </summary>
    public IReadOnlyList<int> HiddenModels { get; }

    /// <summary>True for a world object kept out of sight until <see cref="OpeningSettings.Settle"/> (cargo the crash spills).</summary>
    public bool Hides(WorldObjectPlacement placement) => _settings.HiddenObjects.Contains(placement.Definition.Id ?? "") && NearCentre(placement.Position);

    /// <summary>True within <see cref="OpeningSettings.HideRadius"/> of the POI's centre (x and z), where site fires wait for the crash.</summary>
    public bool NearCentre(Vector3 at) => Vector2.Distance(new Vector2(at.X, at.Z), _poi.Centre) <= _settings.HideRadius;

    /// <summary>The POI it plays at.</summary>
    public PoiInstance Poi => _poi;

    /// <summary>Where the wreck (its first piece's model) lies: the flying ship's last pose.</summary>
    public Pose Wreck { get; }

    /// <summary>Where each piece's model lies.</summary>
    public IReadOnlyList<Pose> PieceWrecks { get; }

    /// <summary>Where the housing's model lies.</summary>
    public Pose HousingWreck { get; }

    /// <summary>Where each container's model lies.</summary>
    public IReadOnlyList<Pose> ContainerWrecks { get; }

    /// <summary>When each container breaks away from the ship; it lands <see cref="OpeningContainers.Fall"/> seconds later.</summary>
    public IReadOnlyList<float> Releases => _release;

    /// <summary>The settings it plays.</summary>
    public OpeningSettings Settings => _settings;

    /// <summary>A point given relative to the POI, in world space.</summary>
    public Vector3 Local(float[] at) => _poi.ToWorld(new Vector3(at[0], at[1], at[2]));

    /// <summary>
    /// The flying ship's origin at <paramref name="t"/>: a smooth curve through the keys, raised over hills in its way,
    /// still after the last key.
    /// </summary>
    public Pose Ship(float t)
    {
        var keyed = Keyed(t);
        if (_lift.Length == 0 || t >= _settings.Touchdown)
            return keyed;
        var at = Math.Clamp(t / LiftStep, 0f, _lift.Length - 1.001f);
        var k = (int)at;
        var lift = _lift[k] + (_lift[k + 1] - _lift[k]) * (at - k);
        return keyed with { Position = keyed.Position + new Vector3(0, lift, 0) };
    }

    // The curve through the keys alone.
    private Pose Keyed(float t)
    {
        if (t <= _ship[0].T)
            return _ship[0].Pose;
        if (t >= _ship[^1].T)
            return _ship[^1].Pose;
        var i = 0;
        while (_ship[i + 1].T < t)
            i++;
        var (t1, a) = _ship[i];
        var (t2, b) = _ship[i + 1];
        var s = (t - t1) / (t2 - t1);
        // Catmull-Rom tangents scaled to the uneven key times; the ship stops dead at the last key.
        var m1 = Tangent(i);
        var m2 = i + 1 == _ship.Count - 1 ? Vector3.Zero : Tangent(i + 1);
        var h = t2 - t1;
        var position = Hermite(a.Position, m1 * h, b.Position, m2 * h, s);
        // Turns ease in and out between keys.
        return new Pose(position, Quaternion.Normalize(Quaternion.Slerp(a.Rotation, b.Rotation, Smooth(s))));
    }

    /// <summary>How fast the ship moves at <paramref name="t"/>, in metres per second.</summary>
    public Vector3 ShipVelocity(float t)
    {
        const float dt = 0.02f;
        return (Ship(t + dt).Position - Ship(t - dt).Position) / (2 * dt);
    }

    /// <summary>Where everything is at <paramref name="t"/>.</summary>
    public OpeningPieces Pieces(float t)
    {
        var ship = Ship(t);
        // The pieces fly as one until the break, then each comes to rest where its model lies.
        var apart = Smooth(Math.Clamp((t - _settings.Break) / (_settings.Settle - _settings.Break), 0f, 1f));
        var pieces = t >= _settings.Settle ? PieceWrecks.ToArray()
            : _piecesIntact.Select(intact => ship.Compose(Lerp(intact, Pose.Identity, apart))).ToArray();
        return new OpeningPieces(ship, pieces, Housing(t), [.. Enumerable.Range(0, _release.Length).Select(i => Container(i, t))]);
    }

    /// <summary>Where the explosion is at <paramref name="t"/>: on the flying ship.</summary>
    public Vector3 Blast(float t) => Ship(t).Apply(FromBlender(_settings.ExplodeAt));

    /// <summary>The housing model's origin at <paramref name="t"/>: on the ship, then tumbling down to where it lies.</summary>
    public Pose Housing(float t) =>
        Falling(t, _settings.Explode, _settings.HousingDown, _housingOnShip, HousingWreck, _settings.HousingTurns);

    /// <summary>Container <paramref name="i"/>'s model origin at <paramref name="t"/>: in its rack, then falling to where it lies.</summary>
    public Pose Container(int i, float t) =>
        Falling(t, _release[i], _release[i] + _settings.Containers.Fall, _containersOnShip[i], ContainerWrecks[i], _settings.Containers.Turns);

    // Something riding on the ship (at onShip) until off, then falling to where it lies by down, tumbling whole turns.
    private Pose Falling(float t, float off, float down, Pose onShip, Pose lying, int turns)
    {
        if (t <= off)
            return Ship(t).Compose(onShip);
        if (t >= down)
            return lying;
        var start = Ship(off).Compose(onShip);
        var span = down - off;
        var s = (t - off) / span;
        // It leaves with the ship's speed and falls; Hermite keeps both ends exact.
        var leave = ShipVelocity(off) * span;
        var land = new Vector3(leave.X * 0.3f, -Math.Abs(leave.Length()) * 0.6f, leave.Z * 0.3f);
        var position = Hermite(start.Position, leave, lying.Position, land, s);
        var horizontal = new Vector3(leave.X, 0, leave.Z);
        var axis = horizontal.LengthSquared() > 1e-6f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, horizontal)) : Vector3.UnitX;
        // Whole turns, so the tumble ends exactly on the lying pose.
        var spin = Quaternion.CreateFromAxisAngle(axis, -MathF.Tau * turns * Smooth(s));
        var turn = Quaternion.Normalize(spin * Quaternion.Slerp(start.Rotation, lying.Rotation, s));
        return new Pose(position, turn);
    }

    /// <summary>The camera at <paramref name="t"/>, with no shake; null before the first shot or after the last.</summary>
    public OpeningCamera? Camera(float t)
    {
        if (_settings.Shots.FirstOrDefault(s => t >= s.Start && t < s.End) is not { } shot)
            return null;
        var s = (t - shot.Start) / (shot.End - shot.Start);
        var ship = Ship(t);
        var heading = Heading(ship.Rotation);
        var aim = ship.Apply(new Vector3(0, 2.5f, 0));
        Vector3 position;
        Vector3 target;
        if (shot.Follow is { } follow)
        {
            position = ship.Position + Vector3.Transform(Vec(follow), heading);
            target = shot.Ahead is { } ahead ? ship.Position + Vector3.Transform(Vec(ahead), heading) : aim;
        }
        else
        {
            position = Vector3.Lerp(Local(shot.From!), Local(shot.FromEnd ?? shot.From!), s);
            target = shot.Look is { } look ? Vector3.Lerp(Local(look), Local(shot.LookEnd ?? look), s) : aim;
        }
        // Never under the ground (hills differ from world to world).
        if (_ground is not null)
            position.Y = Math.Max(position.Y, _ground(position.X, position.Z) + shot.Clear);
        return new OpeningCamera(position, target, shot.Fov);
    }

    /// <summary>How far the camera shakes at <paramref name="t"/>, in metres.</summary>
    public float Shake(float t) => _settings.Shakes
        .Where(k => t >= k.At && t < k.At + k.Seconds)
        .Select(k => k.Strength * (1f - (t - k.At) / k.Seconds) * (1f - (t - k.At) / k.Seconds))
        .DefaultIfEmpty(0f).Max();

    /// <summary>How dark the picture is at <paramref name="t"/>: 1 black, 0 clear.</summary>
    public float Black(float t)
    {
        var (start, end) = (_settings.Black[0], _settings.Black[1]);
        if (t < _settings.FadeIn)
            return 1f - t / _settings.FadeIn;
        if (t < start)
            return 0f;
        if (t < end)
            return Math.Clamp((t - start) / Math.Max(0.01f, (end - start) * 0.6f), 0f, 1f);
        return Math.Clamp(1f - (t - end) / _settings.PlayerFadeIn, 0f, 1f);
    }

    /// <summary>True from when the camera is the player's own: after the picture went black.</summary>
    public bool OnPlayer(float t) => t >= _settings.Black[1];

    // The ship's turn without its pitch and roll: what a follow shot rides with, so the horizon stays level.
    private static Quaternion Heading(Quaternion rotation)
    {
        var nose = Vector3.Transform(-Vector3.UnitZ, rotation);
        var yaw = MathF.Atan2(-nose.X, -nose.Z);
        return Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
    }

    private Pose KeyPose(OpeningKey k)
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _poi.Yaw + Radians(k.Yaw))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, Radians(k.Pitch))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Radians(k.Roll));
        return new Pose(Local(k.At), Quaternion.Normalize(turn));
    }

    private Pose Placed(PoiModel model) => new(
        _poi.ToWorld(new Vector3(model.At.X, model.At.Y, model.At.Z)),
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, _poi.Yaw + Radians(model.Yaw)));

    private Vector3 Tangent(int i)
    {
        var prev = _ship[Math.Max(0, i - 1)];
        var next = _ship[Math.Min(_ship.Count - 1, i + 1)];
        return next.T > prev.T ? (next.Pose.Position - prev.Pose.Position) / (next.T - prev.T) : Vector3.Zero;
    }

    /// <summary>A Blender-frame placement (x, y forward, z up; degrees XYZ) as a pose in the game's frame (y up, -z forward).</summary>
    public static Pose Blender(OpeningPlacement placement)
    {
        var (rx, ry, rz) = (Radians(placement.Rotation[0]), Radians(placement.Rotation[1]), Radians(placement.Rotation[2]));
        // Blender's x, y and z axes are the game's x, -z and y; XYZ order applies x first.
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, rz)
            * Quaternion.CreateFromAxisAngle(-Vector3.UnitZ, ry)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rx);
        return new Pose(FromBlender(placement.At), Quaternion.Normalize(turn));
    }

    /// <summary>A Blender-frame point in the game's frame.</summary>
    public static Vector3 FromBlender(float[] at) => new(at[0], at[2], -at[1]);

    private static Vector3 Vec(float[] at) => new(at[0], at[1], at[2]);

    private static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float s)
    {
        var s2 = s * s;
        var s3 = s2 * s;
        return (2 * s3 - 3 * s2 + 1) * p0 + (s3 - 2 * s2 + s) * m0 + (-2 * s3 + 3 * s2) * p1 + (s3 - s2) * m1;
    }

    private static Pose Lerp(Pose a, Pose b, float s) =>
        new(Vector3.Lerp(a.Position, b.Position, s), Quaternion.Normalize(Quaternion.Slerp(a.Rotation, b.Rotation, s)));

    private static float Smooth(float s) => s * s * (3 - 2 * s);

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;
}
