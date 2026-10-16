using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Creations;

/// <summary>
/// Reads and writes a <see cref="Creation"/> as JSON (D8): saves, blueprints and late-join sync all use it.
/// The graph is stored (parts and edges); Bodies are derived on load. Written in canonical form (parts
/// by id, edges in <see cref="Creation.Connections"/> order), so a round-trip is byte for byte.
/// <code>
/// {
///   "format_version": 1,
///   "id": 7,
///   "authority_peer": 1,
///   "parts": [ { "id": 1, "part": "vanilla:wood_block", "cell": [0, 0, 0], "rotation": "+x+y" } ],
///   "connections": [ { "type": "rotational", "a": 2, "a_socket": "rotor", "b": 3 } ]
/// }
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
            Parts = [.. creation.Parts.Select(p => new PartInstanceData
            {
                Id = p.Id,
                Part = p.Part,
                Cell = p.Cell,
                Rotation = p.Rotation,
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

        var creation = new Creation(id, catalog, peer) { Extra = data.Extra };
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
        return creation;
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
        if (sink.ErrorCount > errors)
            return null;
        KnownKeys.WarnUnknown(data.Extra, path, sink, typeof(PartInstanceData));
        var part = new PartInstance(data.Id!.Value, Identifiers.Qualify(data.Part!), data.Cell!.Value, data.Rotation ?? GridRotation.Identity)
        {
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

        public List<PartInstanceData>? Parts { get; init; }

        public List<ConnectionData>? Connections { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    internal sealed record PartInstanceData
    {
        public int? Id { get; init; }

        public string? Part { get; init; }

        public GridCell? Cell { get; init; }

        public GridRotation? Rotation { get; init; }

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
}
