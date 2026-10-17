using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Creations;

/// <summary>
/// Reads and writes a <see cref="Creation"/> as JSON (D8): saves, blueprints and late-join sync all use it.
/// The graph is stored (parts and edges); Bodies are derived on load. Each Body with a known world pose
/// gets a <c>bodies</c> entry naming its lowest part. Written in canonical form (parts by id, edges in
/// <see cref="Creation.Connections"/> order, bodies in <see cref="Creation.Bodies"/> order), so a
/// round-trip is byte for byte.
/// <code>
/// {
///   "format_version": 1,
///   "id": 7,
///   "authority_peer": 1,
///   "on_lift": true,
///   "parts": [ { "id": 1, "part": "vanilla:wood_block", "cell": [0, 0, 0], "rotation": "+x+y", "color": "#d02020", "damage": 10 } ],
///   "connections": [ { "type": "rotational", "a": 2, "a_socket": "rotor", "b": 3 } ],
///   "bodies": [ { "part": 1, "position": [0, 2.5, 0], "rotation": [0, 0, 0, 1] } ]
/// }
/// </code>
/// A body's <c>position</c> (metres) and <c>rotation</c> (unit quaternion <c>[x, y, z, w]</c>) place its
/// grid frame in the world (<see cref="Pose"/>). Linear and angular velocity are not stored yet.
/// <c>on_lift</c> (written only when true) marks a Creation a Lift holds; a part's <c>color</c> is its
/// Paint Tool colour and <c>damage</c> the health it has lost, each written only when set.
/// <code>
/// </code>
/// </summary>
public static class CreationJson
{
    /// <summary>The format version this build reads and writes.</summary>
    public const int FormatVersion = 1;

    /// <summary>Serializes a Creation in canonical form.</summary>
    public static string Write(Creation creation)
    {
        var data = new CreationData
        {
            FormatVersion = FormatVersion,
            Id = creation.Id,
            AuthorityPeer = creation.AuthorityPeer,
            OnLift = creation.OnLift ? true : null,
            Parts = [.. creation.Parts.Select(p => new PartInstanceData
            {
                Id = p.Id,
                Part = p.Part,
                Cell = p.Cell,
                Rotation = p.Rotation,
                Color = p.Color,
                Damage = creation.PartDamage(p.Id) is var damage and > 0 ? damage : null,
                Extra = Sorted(p.Extra),
            })],
            Connections = [.. creation.Connections.Select(c => new ConnectionData
            {
                Type = c.Type,
                A = c.A,
                ASocket = c.ASocket,
                B = c.B,
                BSocket = c.BSocket,
            })],
            Bodies = BodyPoses(creation),
            Extra = Sorted(creation.Extra),
        };
        return JsonSerializer.Serialize(data, DataJson.WriteOptions) + "\n";
    }

    /// <summary>
    /// Parses and validates a saved Creation against <paramref name="catalog"/>. Every problem is added to
    /// <paramref name="diagnostics"/> (with <paramref name="file"/> as its location); returns null on any error.
    /// </summary>
    public static Creation? Read(string json, IPartCatalog catalog, string file, List<DataDiagnostic> diagnostics)
    {
        var sink = new DiagnosticSink(file, diagnostics);
        var data = ModLoader.ParseJson<CreationData>(json, sink);
        if (data is null)
            return null;
        const string invalid = DiagnosticCodes.CreationInvalid;
        KnownKeys.WarnUnknown(data.Extra, "$", sink, typeof(CreationData));

        if (data.FormatVersion is null)
            sink.Error(invalid, "$.format_version", "format_version is required.");
        else if (data.FormatVersion != FormatVersion)
            sink.Error(DiagnosticCodes.UnsupportedFormat, "$.format_version",
                $"format_version {data.FormatVersion} is not supported; this build reads {FormatVersion}.");
        if (data.Id is not { } id)
        {
            sink.Error(invalid, "$.id", "id is required.");
            id = 0;
        }
        var peer = data.AuthorityPeer ?? Creation.HostPeer;
        if (peer < 1)
            sink.Error(invalid, "$.authority_peer", "authority_peer must be 1 or more.");
        if (data.Parts is not { Count: > 0 })
            sink.Error(invalid, "$.parts", "a creation needs at least one part.");
        if (sink.ErrorCount > 0)
            return null;

        var creation = new Creation(id, catalog, peer) { Extra = data.Extra, OnLift = data.OnLift == true };
        var owner = new Dictionary<GridCell, int>();
        for (var i = 0; i < data.Parts!.Count; i++)
        {
            var path = $"$.parts[{i}]";
            if (ReadPart(data.Parts[i], path, catalog, sink) is not var (part, definition))
                continue;
            if (creation.FindPart(part.Id) is not null)
            {
                sink.Error(invalid, $"{path}.id", $"part id {part.Id} is used twice.");
                continue;
            }
            var cells = PartGeometry.Cells(definition, part.Cell, part.Rotation).ToList();
            var clashes = cells.Where(owner.ContainsKey).Take(1).ToList();
            if (clashes is [var clash] && owner[clash] is var other)
            {
                sink.Error(DiagnosticCodes.CreationOverlap, path, $"part {part.Id} overlaps part {other} at {clash}.");
                continue;
            }
            foreach (var c in cells)
                owner[c] = part.Id;
            creation.Load(part, definition);
            if (data.Parts[i].Damage is { } damage)
                creation.SetPartDamage(part.Id, damage);
        }
        if (sink.ErrorCount > 0)
            return null;

        var seen = new HashSet<Connection>();
        for (var i = 0; i < (data.Connections?.Count ?? 0); i++)
        {
            var path = $"$.connections[{i}]";
            var c = data.Connections![i];
            foreach (var key in c.Extra?.Keys ?? Enumerable.Empty<string>())
                sink.Warning(DiagnosticCodes.UnknownKey, $"{path}.{key}", $"unknown key '{key}' on a connection is ignored and not saved.");
            if (c.Type is not { } type || c.A is not { } a || c.B is not { } b)
            {
                sink.Error(invalid, path, "a connection needs type, a and b.");
                continue;
            }
            var connection = new Connection(type, a, b, c.ASocket, c.BSocket);
            if (!seen.Add(connection))
                sink.Error(DiagnosticCodes.CreationConnection, path, $"connection {a} → {b} is listed twice.");
            else if (!creation.TryConnect(connection, out var error))
                sink.Error(DiagnosticCodes.CreationConnection, path, error);
        }
        if (sink.ErrorCount > 0)
            return null;

        var pieces = creation.Components(includeJoints: true);
        if (pieces.Count > 1)
        {
            sink.Error(DiagnosticCodes.CreationDisconnected, "$.connections",
                $"the parts form {pieces.Count} separate pieces (lowest ids {string.Join(", ", pieces.Select(p => p[0]))}); a creation must be connected.");
            return null;
        }
        ReadBodies(data.Bodies, creation, sink);
        return sink.ErrorCount > 0 ? null : creation;
    }

    private static List<BodyPoseData>? BodyPoses(Creation creation)
    {
        var poses = new List<BodyPoseData>();
        foreach (var body in creation.Bodies.Where(b => creation.HasBodyPose(b.Index)))
        {
            var pose = creation.BodyPose(body.Index);
            poses.Add(new BodyPoseData { Part = body.PartIds[0], Position = pose.Position.ToVec3(), Rotation = pose.Rotation });
        }
        return poses.Count > 0 ? poses : null;
    }

    private static void ReadBodies(List<BodyPoseData>? bodies, Creation creation, DiagnosticSink sink)
    {
        const string invalid = DiagnosticCodes.CreationInvalid;
        var claimed = new Dictionary<int, int>();
        for (var i = 0; i < (bodies?.Count ?? 0); i++)
        {
            var path = $"$.bodies[{i}]";
            var b = bodies![i];
            foreach (var key in b.Extra?.Keys ?? Enumerable.Empty<string>())
                sink.Warning(DiagnosticCodes.UnknownKey, $"{path}.{key}", $"unknown key '{key}' on a body is ignored and not saved.");
            if (b.Part is not { } part || b.Position is not { } position || b.Rotation is not { } rotation)
            {
                sink.Error(invalid, path, "a body needs part, position and rotation.");
                continue;
            }
            if (creation.FindPart(part) is null)
            {
                sink.Error(invalid, $"{path}.part", $"body names part {part}, which is not in the creation.");
                continue;
            }
            var length = rotation.Length();
            if (!float.IsFinite(length) || MathF.Abs(length - 1) > 1e-3f)
            {
                sink.Error(invalid, $"{path}.rotation", $"rotation must be a unit quaternion [x, y, z, w]; its length is {length}.");
                continue;
            }
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            {
                sink.Error(invalid, $"{path}.position", "position must be finite.");
                continue;
            }
            var body = creation.BodyIndexOf(part);
            if (claimed.TryGetValue(body, out var first))
            {
                sink.Error(invalid, $"{path}.part", $"parts {first} and {part} are in the same body, so only one may carry its pose.");
                continue;
            }
            claimed[body] = part;
            creation.SetBodyPose(body, new Pose(position.ToNumerics(), Quaternion.Normalize(rotation)));
        }
    }

    private static (PartInstance, PartDefinition)? ReadPart(PartInstanceData data, string path, IPartCatalog catalog, DiagnosticSink sink)
    {
        const string invalid = DiagnosticCodes.CreationInvalid;
        var errors = sink.ErrorCount;
        if (data.Id is not ({ } id and >= 1))
            sink.Error(invalid, $"{path}.id", "part id must be 1 or more.");
        if (data.Cell is null)
            sink.Error(invalid, $"{path}.cell", "cell is required.");
        PartDefinition? definition = null;
        if (string.IsNullOrEmpty(data.Part))
            sink.Error(invalid, $"{path}.part", "part is required.");
        else if ((definition = catalog.FindDefinition(data.Part)) is null)
            sink.Error(DiagnosticCodes.CreationUnknownPart, $"{path}.part", $"unknown part '{data.Part}'; is its mod loaded?");
        else if (!definition.IsPlaceable)
            sink.Error(invalid, $"{path}.part", $"'{data.Part}' cannot be placed.");
        if (data.Color is not null && !HexColor.TryParse(data.Color, out _))
            sink.Error(invalid, $"{path}.color", $"color must be \"#rrggbb\" (got \"{data.Color}\").");
        if (data.Damage is { } damage && !(float.IsFinite(damage) && damage >= 0))
            sink.Error(invalid, $"{path}.damage", $"damage must be a number of 0 or more (got {damage}).");
        if (sink.ErrorCount > errors)
            return null;
        KnownKeys.WarnUnknown(data.Extra, path, sink, typeof(PartInstanceData));
        var part = new PartInstance(data.Id!.Value, Identifiers.Qualify(data.Part!), data.Cell!.Value, data.Rotation ?? GridRotation.Identity)
        {
            Color = data.Color?.ToLowerInvariant(),
            Extra = data.Extra,
        };
        return (part, definition!);
    }

    private static Dictionary<string, JsonElement>? Sorted(IReadOnlyDictionary<string, JsonElement>? extra) =>
        extra?.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);

    internal sealed record CreationData
    {
        public int? FormatVersion { get; init; }

        public int? Id { get; init; }

        public int? AuthorityPeer { get; init; }

        public bool? OnLift { get; init; }

        public List<PartInstanceData>? Parts { get; init; }

        public List<ConnectionData>? Connections { get; init; }

        public List<BodyPoseData>? Bodies { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    internal sealed record PartInstanceData
    {
        public int? Id { get; init; }

        public string? Part { get; init; }

        public GridCell? Cell { get; init; }

        public GridRotation? Rotation { get; init; }

        public string? Color { get; init; }

        public float? Damage { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    internal sealed record ConnectionData
    {
        public ConnectionType? Type { get; init; }

        public int? A { get; init; }

        public string? ASocket { get; init; }

        public int? B { get; init; }

        public string? BSocket { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    internal sealed record BodyPoseData
    {
        public int? Part { get; init; }

        public Vec3? Position { get; init; }

        [JsonConverter(typeof(QuaternionJsonConverter))]
        public Quaternion? Rotation { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }
}
