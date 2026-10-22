using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.World;

/// <summary>
/// A derelict vehicle (<c>derelicts/&lt;id&gt;.json</c>): a Creation of real parts the world leaves in a POI, found without
/// some of its key parts. <see cref="Creation"/> is a Creation as a blueprint stores it (the <c>creation</c> object of a
/// file the Lift saved), and <see cref="Missing"/> says what each one placed is found without.
/// <code>
/// {
///   "id": "scrap_buggy",
///   "name": "Scrap Buggy",
///   "missing": [ { "part": "scrap_gas_engine", "chance": 0.85 }, { "part": "scrap_wheel", "count": 1, "count_max": 2 } ],
///   "creation": { "format_version": 1, "id": 0, "parts": [ … ], "connections": [ … ] }
/// }
/// </code>
/// </summary>
public sealed record DerelictDefinition
{
    /// <summary>Id, unique in its mod (lowercase snake_case).</summary>
    public string? Id { get; init; }

    /// <summary>Display name.</summary>
    public string? Name { get; init; }

    /// <summary>Parts each one is found without, rolled per marker from the world seed.</summary>
    public List<DerelictMissing>? Missing { get; init; }

    /// <summary>The Creation, as a blueprint stores it (<see cref="Blueprint"/>).</summary>
    public JsonObject? Creation { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A part a derelict may be found without (<see cref="DerelictDefinition.Missing"/>).</summary>
public sealed record DerelictMissing
{
    /// <summary>The part's id (bare for the derelict's own mod): every copy of it in the Creation can go.</summary>
    public string? Part { get; init; }

    /// <summary>Odds, 0 to 1, that any go at all. Omitted: 1.</summary>
    public float? Chance { get; init; }

    /// <summary>How many copies go when they do. Omitted: 1.</summary>
    public int? Count { get; init; }

    /// <summary>Most copies that go (from <see cref="Count"/> to this, picked from the seed). Omitted: <see cref="Count"/>.</summary>
    public int? CountMax { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A derelict that loaded, by its game-wide id (<c>mod_id:id</c>).</summary>
/// <param name="QualifiedId">Its game-wide id.</param>
/// <param name="ModId">The mod it belongs to, whose parts its bare ids name.</param>
/// <param name="Definition">Its data.</param>
public sealed record LoadedDerelict(string QualifiedId, string ModId, DerelictDefinition Definition);

/// <summary>
/// Derelict vehicles: a POI's <c>derelict</c> marker (<see cref="PoiMarker.DerelictType"/>) puts one there, built from its
/// definition and missing the parts its roll takes (<see cref="Missing"/>). Unlike a world part it belongs to nobody: the
/// host places it once, as an ordinary Creation anyone may strip, drive off or build onto, and records the marker as used
/// (with the world's destroyed objects, <see cref="WorldParts.ObjectOf"/>) so it never places another. Pure logic.
/// </summary>
public static class Derelicts
{
    /// <summary>The mod folder derelicts live in.</summary>
    public const string Folder = "derelicts";

    // Salt of the derelict roll, so it is not the same as the loot roll of the same key.
    private const uint Stream = 0x44524C54u; // "DRLT"

    // How far (m) above the marker its lowest point is put, so it settles onto the ground rather than into it.
    private const float Clearance = 0.02f;

    /// <summary>
    /// Every <c>derelict</c> marker of every POI <paramref name="world"/> placed that names a derelict that loaded, in POI
    /// then marker order, keyed like part markers (<see cref="WorldParts.KeyOf"/>), for <see cref="WorldParts.Reconcile"/>.
    /// </summary>
    public static IReadOnlyList<WorldPartSpot> SpotsOf(WorldGenerator world) =>
    [
        .. world.MarkersOfType(PoiMarker.DerelictType)
            .Where(m => !string.IsNullOrEmpty(m.Marker.Ref))
            .Select(m => (Marker: m, Id: PoiMarker.Qualify(m.Marker.Ref!, m.Poi.Poi.QualifiedId)))
            .Where(m => world.Data.Derelicts.ContainsKey(m.Id))
            .Select(m => new WorldPartSpot(WorldParts.KeyOf(m.Marker.Poi.Poi.QualifiedId, m.Marker.Index, m.Marker.Poi.Index), m.Marker.Poi.Poi.QualifiedId,
                m.Id, m.Marker.Position, m.Marker.Yaw, Derelict: world.Data.Derelicts[m.Id])),
    ];

    /// <summary>
    /// The Creation <paramref name="derelict"/> holds, as a new host-simulated Creation with id <paramref name="id"/>,
    /// validated against <paramref name="catalog"/>; null with diagnostics when it does not load.
    /// </summary>
    public static Creation? Read(LoadedDerelict derelict, IPartCatalog catalog, int id, string file, List<DataDiagnostic> diagnostics)
    {
        if (derelict.Definition.Creation is not { } shape)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.CreationInvalid, file, "$.creation", null,
                "creation is required: the Creation as a blueprint stores it."));
            return null;
        }
        var envelope = new JsonObject
        {
            ["format_version"] = Blueprint.FormatVersion,
            ["name"] = derelict.Definition.Name ?? derelict.QualifiedId,
            ["creation"] = shape.DeepClone(),
        };
        return Blueprint.Read(envelope.ToJsonString(), catalog, id, file, diagnostics);
    }

    /// <summary>
    /// The ids of the parts of <paramref name="creation"/> that <paramref name="derelict"/>'s roll with
    /// <paramref name="seed"/> takes away: each <see cref="DerelictMissing"/> entry, if its chance comes up, takes from its
    /// count to its count max of the copies of its part, picked at random. The same seed always takes the same parts.
    /// </summary>
    public static IReadOnlyList<int> Missing(LoadedDerelict derelict, Creation creation, uint seed)
    {
        var rng = new Random((int)seed);
        var gone = new List<int>();
        foreach (var rule in derelict.Definition.Missing ?? [])
        {
            if (rule.Part is not { } part)
                continue;
            var chance = rule.Chance ?? 1f;
            var count = Math.Max(0, rule.Count ?? 1);
            var countMax = Math.Max(count, rule.CountMax ?? count);
            // Always draw both, so one entry's chance does not shift the next entry's roll.
            var hit = rng.NextSingle() < chance;
            var take = rng.Next(count, countMax + 1);
            var id = Qualify(part, derelict.ModId);
            var copies = creation.Parts.Where(p => p.Part == id && !gone.Contains(p.Id)).Select(p => p.Id).Order().ToList();
            if (!hit)
                continue;
            for (var i = 0; i < take && copies.Count > 0; i++)
            {
                var pick = rng.Next(copies.Count);
                gone.Add(copies[pick]);
                copies.RemoveAt(pick);
            }
        }
        return gone;
    }

    /// <summary>The roll seed of marker <paramref name="key"/> (<see cref="WorldParts.KeyOf"/>) in the world <paramref name="worldSeed"/>.</summary>
    public static uint SeedOf(long worldSeed, string key) => Noise.Mix(Noise.Salt(worldSeed, Stream) ^ Noise.Hash(key));

    /// <summary>
    /// The pose of <paramref name="creation"/>'s grid frame that stands the middle of the bottom of its parts' cells on
    /// <paramref name="floor"/> (a hair above it), turned by <paramref name="yaw"/> radians about +Y.
    /// </summary>
    public static Pose PoseFor(Creation creation, Vector3 floor, float yaw)
    {
        var box = WorldBounds.Empty;
        foreach (var body in CreationBounds.LocalBodyBounds(creation))
            box = box.Union(body);
        var bottom = new Vector3((box.Min.X + box.Max.X) / 2f, box.Min.Y, (box.Min.Z + box.Max.Z) / 2f);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        return new Pose(floor + new Vector3(0, Clearance, 0) - Vector3.Transform(bottom, rotation), rotation);
    }

    /// <summary>
    /// The derelict for <paramref name="spot"/>: its Creation with id <paramref name="id"/>, without the parts its roll
    /// takes, posed on the marker, at rest and owned by nobody. Null with <paramref name="problem"/> when it does not load
    /// or the roll would leave it in pieces.
    /// </summary>
    public static Creation? Build(WorldPartSpot spot, LoadedDerelict derelict, int id, IPartCatalog catalog, long worldSeed, out string? problem)
    {
        var diagnostics = new List<DataDiagnostic>();
        if (Read(derelict, catalog, id, $"{derelict.QualifiedId} ({spot.Key})", diagnostics) is not { } creation)
        {
            problem = $"{spot.Key}: {derelict.QualifiedId} does not load ({string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message))})";
            return null;
        }
        foreach (var part in Missing(derelict, creation, SeedOf(worldSeed, spot.Key)))
        {
            if (creation.RemovePart(part, () => -1).SplitOff.Count > 0)
            {
                problem = $"{spot.Key}: taking part {part} off {derelict.QualifiedId} leaves it in pieces";
                return null;
            }
        }
        creation.SetAllBodyPoses(PoseFor(creation, spot.Floor, spot.Yaw));
        problem = null;
        return creation;
    }

    /// <summary>
    /// Checks <paramref name="derelict"/> (from <paramref name="file"/>): a name, a Creation that loads against
    /// <paramref name="catalog"/>, and missing parts it has, each of which comes off without leaving the rest in pieces.
    /// Returns what is wrong; an error means it does not load.
    /// </summary>
    public static IReadOnlyList<DataDiagnostic> Validate(LoadedDerelict derelict, IPartCatalog catalog, string file)
    {
        var diagnostics = new List<DataDiagnostic>();
        Validate(derelict, catalog, new DiagnosticSink(file, diagnostics), diagnostics, file);
        return diagnostics;
    }

    private static void Validate(LoadedDerelict derelict, IPartCatalog catalog, DiagnosticSink sink, List<DataDiagnostic> diagnostics, string file)
    {
        const string code = DiagnosticCodes.WorldGenInvalid;
        var definition = derelict.Definition;
        KnownKeys.WarnUnknown(definition.Extra, "$", sink, typeof(DerelictDefinition));
        if (string.IsNullOrWhiteSpace(definition.Name))
            sink.Error(code, "$.name", "a derelict needs a name.");
        var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var creation = Read(derelict, catalog, 0, file, diagnostics);
        if (creation is null)
        {
            if (diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error) == errors)
                sink.Error(code, "$.creation", "the creation does not load.");
            return;
        }
        if (creation.Parts.Count == 0)
            sink.Error(code, "$.creation.parts", "a derelict needs parts.");
        for (var i = 0; i < (definition.Missing?.Count ?? 0); i++)
        {
            var rule = definition.Missing![i];
            var path = $"$.missing[{i}]";
            KnownKeys.WarnUnknown(rule.Extra, path, sink, typeof(DerelictMissing));
            if (rule.Chance is { } chance && !(chance >= 0f && chance <= 1f))
                sink.Error(code, $"{path}.chance", "chance must be from 0 to 1.");
            if (rule.Count is < 0 || rule.CountMax < (rule.Count ?? 1))
                sink.Error(code, $"{path}.count", "count must be 0 or more and count_max at least count.");
            if (rule.Part is not { } part)
            {
                sink.Error(code, $"{path}.part", "part is required: the id of a part the derelict may be found without.");
                continue;
            }
            var id = Qualify(part, derelict.ModId);
            var copies = creation.Parts.Where(p => p.Part == id).Select(p => p.Id).ToList();
            if (copies.Count == 0)
                sink.Error(code, $"{path}.part", $"the creation has no '{id}' to be missing.");
            foreach (var copy in copies)
            {
                var probe = Read(derelict, catalog, 0, file, [])!;
                if (probe.RemovePart(copy, () => -1).SplitOff.Count > 0)
                    sink.Error(code, $"{path}.part", $"taking '{id}' (part {copy}) off leaves the rest in pieces: only parts that hang off it alone can be missing.");
            }
        }
    }

    private static string Qualify(string id, string modId) => id.Contains(':') ? id : $"{modId}:{id}";
}
