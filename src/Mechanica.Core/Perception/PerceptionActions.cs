using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.World;

namespace Mechanica.Core.Perception;

/// <summary>
/// Read-only queries of the world for agents, scripts and tools (AI support PR 3). They change nothing, so the
/// dead gate and recordings skip them:
/// <list type="bullet">
/// <item><c>GetPlayers</c>, <c>GetPlayer</c>: every player's pose, look, velocity, seat, held tool, Lift and health.</item>
/// <item><c>Raycast</c>: the first thing along a ray (by default: what the caller looks at).</item>
/// <item><c>SampleTerrain</c>: ground height, biome, slope and water at points or on a grid.</item>
/// <item><c>GetObjects</c>, <c>GetDrops</c>: standing trees and rocks, and item piles, near a point.</item>
/// <item><c>GetPois</c>: revealed POIs with their markers (all of them for non-players).</item>
/// <item><c>GetWorld</c>: seed, mode, difficulty, clock, tick, size, sea level and who is playing.</item>
/// </list>
/// The limits below bound the work one call can ask of the host; they are API limits, not balance.
/// </summary>
public sealed class PerceptionActions(IPerceptionWorld world)
{
    /// <summary>Action name: every player.</summary>
    public const string GetPlayers = "GetPlayers";

    /// <summary>Action name: one player.</summary>
    public const string GetPlayer = "GetPlayer";

    /// <summary>Action name: cast a ray.</summary>
    public const string Raycast = "Raycast";

    /// <summary>Action name: sample the ground.</summary>
    public const string SampleTerrain = "SampleTerrain";

    /// <summary>Action name: world objects near a point.</summary>
    public const string GetObjects = "GetObjects";

    /// <summary>Action name: item piles on the ground.</summary>
    public const string GetDrops = "GetDrops";

    /// <summary>Action name: POIs.</summary>
    public const string GetPois = "GetPois";

    /// <summary>Action name: the world's settings and clock.</summary>
    public const string GetWorld = "GetWorld";

    /// <summary>Range of a <c>Raycast</c> that names none, in m.</summary>
    public const float DefaultRayRange = 100f;

    /// <summary>Longest <c>Raycast</c>, in m.</summary>
    public const float MaxRayRange = 1000f;

    /// <summary>Most samples one <c>SampleTerrain</c> call returns.</summary>
    public const int MaxSamples = 4096;

    /// <summary>Widest <c>GetObjects</c> search, in m.</summary>
    public const float MaxObjectRadius = 256f;

    /// <summary>Most objects one <c>GetObjects</c> call returns.</summary>
    public const int MaxObjects = 1000;

    /// <summary>Registers all eight queries.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<NoArgs>(GetPlayers, (_, _) => null, (_, _) => Value(world.Players.OrderBy(p => p.Peer).ToList()), readOnly: true);
        dispatcher.Register<GetPlayerArgs>(GetPlayer, (c, a) => FindPlayer(a.Peer ?? c.Peer) is null
            ? a.Peer is { } peer ? $"There is no player {peer}." : "You have no player; pass peer."
            : null,
            (c, a) => Value(FindPlayer(a.Peer ?? c.Peer)), readOnly: true);
        dispatcher.Register<RaycastArgs>(Raycast, ValidateRay, ApplyRay, readOnly: true);
        dispatcher.Register<SampleTerrainArgs>(SampleTerrain, ValidateSample, (_, a) => Value(Sample(a)), readOnly: true);
        dispatcher.Register<GetObjectsArgs>(GetObjects, ValidateObjects, (c, a) => Value(Objects(c, a)), readOnly: true);
        dispatcher.Register<GetDropsArgs>(GetDrops, ValidateDrops, (c, a) => Value(Drops(c, a)), readOnly: true);
        dispatcher.Register<GetPoisArgs>(GetPois, ValidatePois, (_, a) => Value(Pois(a.All)), readOnly: true);
        dispatcher.Register<NoArgs>(GetWorld, (_, _) => world.Generator is null ? NotLoaded : null, (_, _) => Value(Describe()), readOnly: true);
    }

    private const string NotLoaded = "The world isn't made yet.";

    private PlayerView? FindPlayer(int peer) => world.Players.FirstOrDefault(p => p.Peer == peer);

    private string? ValidateRay(ActionCaller caller, RaycastArgs args)
    {
        if (!float.IsFinite(args.Range) || args.Range <= 0f || args.Range > MaxRayRange)
            return $"range must be more than 0 and at most {MaxRayRange:0} m.";
        if (args.From is { } from && !Finite(from))
            return "from must be finite.";
        if (args.Direction is { } direction && (!Finite(direction) || direction.Length < 1e-6f))
            return "direction must be finite and not zero.";
        if ((args.From is null || args.Direction is null) && FindPlayer(caller.Peer) is null)
            return "You have no player to look from; pass from and direction.";
        return null;
    }

    private JsonElement? ApplyRay(ActionCaller caller, RaycastArgs args)
    {
        var self = FindPlayer(caller.Peer);
        var from = (args.From ?? self!.Eye).ToNumerics();
        var direction = Vector3.Normalize((args.Direction ?? self!.Look).ToNumerics());
        var ignore = args.IgnorePlayer ?? (args.From is null ? caller.Peer : 0);
        if (world.Cast(from, direction, args.Range, ignore) is not { } hit)
            return Value(new RaycastResult(false));
        var distance = Vector3.Distance(from, hit.Point);
        var result = new RaycastResult(true, "ground", hit.Point.ToVec3(), distance);
        if (hit.Player is { } player)
            return Value(result with { Kind = "player", Player = player });
        if (hit.Bot is { } bot)
            return Value(result with { Kind = "bot", Bot = bot });
        if (hit.Creation is { } creation)
        {
            var part = hit.Part is { } id && world.FindCreation(creation)?.FindPart(id) is { } found ? found : null;
            return Value(result with { Kind = "part", Creation = creation, Part = hit.Part, PartType = part?.Part });
        }
        if (hit.Object is { } objectId)
            return Value(result with { Kind = "object", Object = objectId.ToString(), ObjectType = world.Generator?.Find(objectId)?.Definition.Id });
        return Value(result);
    }

    private string? ValidateSample(ActionCaller caller, SampleTerrainArgs args)
    {
        if (world.Generator is null)
            return NotLoaded;
        if ((args.Points is not null) == (args.Min is not null || args.Max is not null))
            return "Pass either points, or min and max (with step).";
        if (args.Points is { } points)
        {
            if (points.Count == 0 || points.Count > MaxSamples)
                return $"points must hold 1 to {MaxSamples} points.";
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i] is not { Length: 2 } p || !float.IsFinite(p[0]) || !float.IsFinite(p[1]))
                    return $"points[{i}] must be [x, z].";
            }
            return null;
        }
        if (args.Min is not { Length: 2 } min || args.Max is not { Length: 2 } max || !min.Concat(max).All(float.IsFinite))
            return "min and max must both be [x, z].";
        if (!float.IsFinite(args.Step) || args.Step <= 0f)
            return "step must be more than 0.";
        if (max[0] < min[0] || max[1] < min[1])
            return "max must not be less than min.";
        var (nx, nz) = GridCount(min, max, args.Step);
        if ((long)nx * nz > MaxSamples)
            return $"That grid has {(long)nx * nz} samples; the most is {MaxSamples}. Use a bigger step or a smaller area.";
        return null;
    }

    private static (long X, long Z) GridCount(float[] min, float[] max, float step) =>
        ((long)MathF.Floor((max[0] - min[0]) / step + 1e-4f) + 1, (long)MathF.Floor((max[1] - min[1]) / step + 1e-4f) + 1);

    /// <summary>The ground at the requested points, as <c>SampleTerrain</c> returns it.</summary>
    public TerrainSamples Sample(SampleTerrainArgs args)
    {
        var generator = world.Generator!;
        IEnumerable<(float X, float Z)> points;
        if (args.Points is { } list)
        {
            points = list.Select(p => (p[0], p[1]));
        }
        else
        {
            var (min, max, step) = (args.Min!, args.Max!, args.Step);
            var (nx, nz) = GridCount(min, max, step);
            points = from j in Enumerable.Range(0, (int)nz) from i in Enumerable.Range(0, (int)nx) select (min[0] + i * step, min[1] + j * step);
        }
        var sea = generator.SeaLevel;
        var samples = points.Select(p =>
        {
            var height = generator.HeightAt(p.X, p.Z);
            return new TerrainSample(p.X, p.Z, height, generator.BiomeAt(p.X, p.Z).Id, generator.SlopeAt(p.X, p.Z),
                MathF.Max(0f, generator.WaterLevelAt(p.X, p.Z) - height), generator.PoisAt(p.X, p.Z).FirstOrDefault()?.Poi.QualifiedId);
        }).ToList();
        return new TerrainSamples(sea, samples);
    }

    private string? ValidateObjects(ActionCaller caller, GetObjectsArgs args)
    {
        if (world.Generator is null)
            return NotLoaded;
        if (!float.IsFinite(args.Radius) || args.Radius <= 0f || args.Radius > MaxObjectRadius)
            return $"radius must be more than 0 and at most {MaxObjectRadius:0} m.";
        if (args.Limit < 1 || args.Limit > MaxObjects)
            return $"limit must be from 1 to {MaxObjects}.";
        return CheckCenter(caller, args.Center);
    }

    private string? CheckCenter(ActionCaller caller, Vec3? center) =>
        center is { } c ? Finite(c) ? null : "center must be finite."
            : FindPlayer(caller.Peer) is null ? "You have no player to search around; pass center." : null;

    private Vector3 CenterOf(ActionCaller caller, Vec3? center) => (center ?? FindPlayer(caller.Peer)!.Position).ToNumerics();

    /// <summary>Standing world objects near a point, nearest first, as <c>GetObjects</c> returns them.</summary>
    public IReadOnlyList<WorldObjectView> Objects(ActionCaller caller, GetObjectsArgs args)
    {
        var generator = world.Generator!;
        var centre = CenterOf(caller, args.Center);
        var r = args.Radius;
        var found = new List<WorldObjectView>();
        foreach (var o in generator.ObjectsIn(centre.X - r, centre.Z - r, centre.X + r, centre.Z + r))
        {
            if (world.State?.IsDestroyed(o.Id) == true || (args.Type is { } type && o.Definition.Id != type))
                continue;
            var distance = Vector3.Distance(centre, o.Position);
            if (distance > r)
                continue;
            var full = o.Definition.Health;
            var drops = (o.Definition.Drops ?? []).Where(d => d.Item is not null).Select(d => new ItemAmount(Identifiers.Qualify(d.Item!), d.Count)).ToList();
            found.Add(new WorldObjectView(o.Id.ToString(), o.Definition.Id!, o.Position.ToVec3(), o.Yaw, o.Scale, distance,
                full is { } f ? MathF.Max(0f, f - (world.State?.DamageOf(o.Id) ?? 0f)) : null, full, o.Definition.Refine, drops, o.Definition.Harvest));
        }
        return [.. found.OrderBy(o => o.Distance).ThenBy(o => o.Id, StringComparer.Ordinal).Take(args.Limit)];
    }

    private string? ValidateDrops(ActionCaller caller, GetDropsArgs args)
    {
        if (args.Radius is { } r && (!float.IsFinite(r) || r <= 0f))
            return "radius must be more than 0.";
        return args.Radius is null && args.Center is null ? null : CheckCenter(caller, args.Center);
    }

    /// <summary>Item piles, nearest first when searching around a point, as <c>GetDrops</c> returns them.</summary>
    public IReadOnlyList<DropView> Drops(ActionCaller caller, GetDropsArgs args)
    {
        Vector3? centre = args.Radius is null && args.Center is null ? null : CenterOf(caller, args.Center);
        var views = world.Drops.Values.Select(d =>
        {
            float? distance = centre is { } c ? Vector3.Distance(c, d.Position.ToNumerics()) : null;
            return new DropView(d.Id, d.Position, d.Items, d.Placed, d.Placed ? null : Math.Max(0, d.Expires - world.Now), distance);
        });
        if (args.Radius is { } radius)
            views = views.Where(d => d.Distance <= radius);
        return [.. centre is null ? views.OrderBy(d => d.Id) : views.OrderBy(d => d.Distance).ThenBy(d => d.Id)];
    }

    private string? ValidatePois(ActionCaller caller, GetPoisArgs args) =>
        world.Generator is null ? NotLoaded
            : args.All && caller.Source == Actions.ActionSource.Player ? "Players only see the POIs the story has revealed." : null;

    /// <summary>The world's POIs, revealed ones only unless <paramref name="all"/>, as <c>GetPois</c> returns them.</summary>
    public IReadOnlyList<PoiView> Pois(bool all)
    {
        var revealed = world.RevealedPois;
        return [.. world.Generator!.Pois
            .Where(p => all || revealed.Contains(p.Poi.QualifiedId))
            .Select(p => new PoiView(p.Index, p.Poi.QualifiedId, p.Poi.Prefab.Name ?? p.Poi.Prefab.Id!, p.ToWorld(Vector3.Zero).ToVec3(), p.Radius, p.Yaw,
                revealed.Contains(p.Poi.QualifiedId),
                [.. (p.Poi.Prefab.Markers ?? []).Where(m => m.Type is not null).Select(m => new PoiMarkerView(m.Type!, m.Ref,
                    p.ToWorld(m.At.ToNumerics()).ToVec3(), p.Yaw + m.Yaw * MathF.PI / 180f))]))];
    }

    /// <summary>The world's settings and clock, as <c>GetWorld</c> returns them.</summary>
    public WorldView Describe()
    {
        var generator = world.Generator!;
        return new WorldView(generator.Seed, generator.Seed.ToString(CultureInfo.InvariantCulture), world.Mode, world.Difficulty, world.FriendlyFire, world.Now,
            world.Tick, world.TickRate, generator.WorldSize, generator.WorldDepth, generator.SeaLevel, generator.Spawn?.ToVec3(),
            [.. world.Players.OrderBy(p => p.Peer).Select(p => new PeerView(p.Peer, p.Name))], world.CreationCount, world.Drops.Count,
            generator.Pois.Count, [.. world.RevealedPois.Order(StringComparer.Ordinal)]);
    }

    private static bool Finite(Vec3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
