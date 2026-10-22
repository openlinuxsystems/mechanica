using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.World;

/// <summary>Where a POI's <c>part</c> marker puts its part.</summary>
/// <param name="Key">Stable key of the marker in this world (<see cref="WorldParts.KeyOf"/>), stored on the Creation.</param>
/// <param name="Poi">The POI's qualified id (<c>mod_id:poi_id</c>).</param>
/// <param name="Part">The part, qualified (a bare marker <c>ref</c> names a part of the POI's mod).</param>
/// <param name="Floor">World point its bottom face stands on: the marker's position.</param>
/// <param name="Yaw">Turn about +Y in radians, the POI's and the marker's (0: the part's front, local -Z, faces -Z).</param>
/// <param name="Loot">What a container's marker fills it with (<see cref="WorldLoot"/>), or null for none.</param>
/// <param name="Derelict">
/// For a <c>derelict</c> marker, the derelict it leaves there (<see cref="Derelicts"/>; <paramref name="Part"/> is then its
/// id), or null for a part marker.
/// </param>
/// <param name="Settings">The marker's values for the part's settings (<see cref="PoiMarker.Settings"/>), or null for its defaults.</param>
public sealed record WorldPartSpot(string Key, string Poi, string Part, Vector3 Floor, float Yaw, WorldPartLoot? Loot = null,
    LoadedDerelict? Derelict = null, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? Settings = null);

/// <summary>What the host does to bring the world's own parts in line with the markers (<see cref="WorldParts.Reconcile"/>).</summary>
/// <param name="Add">New world-owned Creations to add (parked: the streamer brings each in when a player comes near).</param>
/// <param name="Remove">World-owned Creations whose marker is gone (the world's content changed), to remove.</param>
/// <param name="Moved">World-owned Creations put back on their marker (its place changed), already re-posed.</param>
/// <param name="Problems">Markers that place nothing, and why.</param>
/// <param name="Used">
/// Keys of the derelict markers whose derelicts are in <paramref name="Add"/>: the host records each as used
/// (<see cref="WorldParts.ObjectOf"/>), so it places no other.
/// </param>
public sealed record WorldPartPlan(IReadOnlyList<Creation> Add, IReadOnlyList<int> Remove, IReadOnlyList<int> Moved, IReadOnlyList<string> Problems,
    IReadOnlyList<string>? Used = null);

/// <summary>
/// The world's own working parts (a Workshop's Fabricator): every <c>part</c> marker of a placed POI puts one part there,
/// as a normal one-part Creation, so its script and station run exactly as a player's do, but anchored, simulated by the
/// host and world-owned (<see cref="Creation.WorldPart"/>, <see cref="WorldOwned"/>). A player may pick one up: it becomes
/// theirs to put down anywhere, and its marker is recorded as taken (<see cref="ObjectOf"/>, with the world's destroyed
/// objects) so it never places another. Pure logic; the host runs <see cref="Reconcile"/> once when its world starts,
/// after a save is loaded, so each marker not taken has exactly one: one saved with the world is kept (with whatever it
/// holds and crafts), a missing one is made, and none is ever made twice.
/// </summary>
public static class WorldParts
{
    /// <summary>The marker type (<see cref="PoiMarker.PartType"/>).</summary>
    public const string MarkerType = PoiMarker.PartType;

    // How far (m) and how much turn (1 - |q·q'|) a saved one may be off its marker before it is put back.
    private const float PositionSlack = 1e-3f;
    private const float RotationSlack = 1e-6f;

    /// <summary>
    /// The key of marker <paramref name="marker"/> (its index in the prefab's markers) of the POI <paramref name="poiIndex"/>
    /// (its place in <see cref="WorldGenerator.Pois"/>) of type <paramref name="poi"/>: <c>vanilla:outpost#0@12</c>. The same
    /// seed and content give the same keys, so a save finds its parts again.
    /// </summary>
    public static string KeyOf(string poi, int marker, int poiIndex) => $"{poi}#{marker}@{poiIndex}";

    /// <summary>
    /// The world object id (<see cref="WorldObjectId.ForPoi"/>) that records marker <paramref name="key"/> as taken, once a
    /// player has picked its part up, or null when the key is not one <see cref="KeyOf"/> makes.
    /// </summary>
    public static WorldObjectId? ObjectOf(string key)
    {
        var hash = key.LastIndexOf('#');
        var at = key.LastIndexOf('@');
        return hash > 0 && at > hash
            && int.TryParse(key.AsSpan(hash + 1, at - hash - 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var marker)
            && int.TryParse(key.AsSpan(at + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var poi)
            ? WorldObjectId.ForPoi(poi, marker)
            : null;
    }

    /// <summary>Every <c>part</c> marker of every POI <paramref name="world"/> placed, in POI then marker order.</summary>
    public static IReadOnlyList<WorldPartSpot> SpotsOf(WorldGenerator world) =>
    [
        .. world.MarkersOfType(MarkerType)
            .Where(m => !string.IsNullOrEmpty(m.Marker.Ref))
            .Select(m =>
            {
                var key = KeyOf(m.Poi.Poi.QualifiedId, m.Index, m.Poi.Index);
                return new WorldPartSpot(key, m.Poi.Poi.QualifiedId, PoiMarker.Qualify(m.Marker.Ref!, m.Poi.Poi.QualifiedId), m.Position, m.Yaw,
                    WorldLoot.Of(m.Marker, key, world.Data.Definition, world.Seed),
                    Settings: SignText.Resolve(m.Marker.Settings, m.Position, m.Poi.Poi.QualifiedId, world.Pois));
            }),
    ];

    /// <summary>
    /// The pose of the grid frame of a one-part Creation holding <paramref name="definition"/> at cell 0, unturned on its
    /// grid, that stands the part's bottom face centred on <paramref name="spot"/>'s floor point, turned by its yaw.
    /// </summary>
    public static Pose PoseFor(PartDefinition definition, WorldPartSpot spot)
    {
        var size = definition.Size ?? GridSize.One;
        var bottom = PartGeometry.CentreMetres(definition, GridCell.Zero, GridRotation.Identity).ToNumerics()
            - new Vector3(0, size.Y * MechanicaInfo.GridSize / 2f, 0);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, spot.Yaw);
        return new Pose(spot.Floor - Vector3.Transform(bottom, rotation), rotation);
    }

    /// <summary>
    /// The world-owned Creation for <paramref name="spot"/> with id <paramref name="id"/>: its part, anchored and posed
    /// (<see cref="PoseFor"/>), simulated by the host, set to the spot's settings, and a container filled with the spot's loot
    /// (its first fill, <see cref="WorldLoot.Fill"/>). Null with <paramref name="problem"/> when the part is unknown or cannot
    /// be placed, or a setting does not fit it.
    /// </summary>
    public static Creation? Build(WorldPartSpot spot, int id, IPartCatalog catalog, out string? problem)
    {
        var creation = new Creation(id, catalog, Creation.HostPeer) { WorldPart = spot.Key };
        if (!creation.TryPlacePart(spot.Part, GridCell.Zero, GridRotation.Identity, out var placed, out var error))
        {
            problem = $"{spot.Key}: {error}";
            return null;
        }
        creation.SetAnchored(placed.Id, true);
        creation.SetAllBodyPoses(PoseFor(creation.DefinitionOf(placed.Id), spot));
        if (spot.Settings is { Count: > 0 } settings)
        {
            var definition = creation.DefinitionOf(placed.Id);
            if (Logic.PartSettings.Check(definition, settings) is { } bad)
            {
                problem = $"{spot.Key}: {bad}";
                return null;
            }
            creation.SetPartSettings(placed.Id, Logic.PartSettings.Apply(definition, null, settings.ToDictionary(e => e.Key, e => (System.Text.Json.JsonElement?)e.Value)));
        }
        if (spot.Loot is { } loot && creation.InventoryOf(placed.Id) is { } inventory)
            WorldLoot.Fill(inventory, loot, creation.WorldLoot = new WorldLootState());
        problem = null;
        return creation;
    }

    /// <summary>
    /// Matches the world-owned Creations in <paramref name="existing"/> (from a save) to <paramref name="spots"/> by key:
    /// a spot without one gets a new Creation (ids from <paramref name="nextId"/>), unless a player took its part
    /// (<paramref name="taken"/> says so of its <see cref="ObjectOf"/> id); one standing at a spot keeps its contents and is
    /// put back on the marker if it is off it; one whose spot is gone, holds another part, or repeats a key, is removed.
    /// Creations players made are never touched. A derelict marker (<see cref="WorldPartSpot.Derelict"/>) that is not used
    /// yet gets its derelict, rolled from <paramref name="worldSeed"/>, and is listed in <see cref="WorldPartPlan.Used"/>.
    /// Changes nothing but the poses of <see cref="WorldPartPlan.Moved"/>.
    /// </summary>
    public static WorldPartPlan Reconcile(IReadOnlyList<WorldPartSpot> spots, IEnumerable<Creation> existing, IPartCatalog catalog, Func<int> nextId,
        Func<WorldObjectId, bool>? taken = null, long worldSeed = 0)
    {
        var add = new List<Creation>();
        var used = new List<string>();
        var remove = new List<int>();
        var moved = new List<int>();
        var problems = new List<string>();
        var standing = new Dictionary<string, Creation>(StringComparer.Ordinal);
        foreach (var creation in existing.Where(c => c.IsWorldOwned).OrderBy(c => c.Id))
        {
            if (!standing.TryAdd(creation.WorldPart!, creation))
                remove.Add(creation.Id);
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spot in spots)
        {
            if (!seen.Add(spot.Key))
                continue;
            // A derelict is nobody's once placed: it is placed once, when its marker is not used yet.
            if (spot.Derelict is { } derelict)
            {
                if (taken is not null && ObjectOf(spot.Key) is { } usedId && taken(usedId))
                    continue;
                if (Derelicts.Build(spot, derelict, nextId(), catalog, worldSeed, out var why) is { } left)
                {
                    add.Add(left);
                    used.Add(spot.Key);
                }
                else
                    problems.Add(why!);
                continue;
            }
            if (standing.Remove(spot.Key, out var found))
            {
                if (found.Parts.Count == 1 && Identifiers.Qualify(found.Parts.First().Part) == Identifiers.Qualify(spot.Part))
                {
                    var pose = PoseFor(found.DefinitionOf(found.Parts.First().Id), spot);
                    var now = found.BodyPose(0);
                    if (Vector3.Distance(now.Position, pose.Position) > PositionSlack
                        || 1f - MathF.Abs(Quaternion.Dot(now.Rotation, pose.Rotation)) > RotationSlack)
                    {
                        found.SetAllBodyPoses(pose);
                        moved.Add(found.Id);
                    }
                    continue;
                }
                // The marker names another part now: the old one goes and the new one comes.
                remove.Add(found.Id);
            }
            if (taken is not null && ObjectOf(spot.Key) is { } id && taken(id))
                continue;
            if (Build(spot, nextId(), catalog, out var problem) is { } made)
                add.Add(made);
            else
                problems.Add(problem!);
        }
        remove.AddRange(standing.Values.Select(c => c.Id));
        remove.Sort();
        return new WorldPartPlan(add, remove, moved, problems, used);
    }
}
