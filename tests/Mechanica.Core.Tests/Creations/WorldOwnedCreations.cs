using System.Text.Json.Nodes;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Creations;

/// <summary>Makes test Creations world-owned (<see cref="Creation.WorldPart"/>), as a POI part marker's are.</summary>
internal static class WorldOwnedCreations
{
    /// <summary>The marker key the copies stand at.</summary>
    public const string Key = "test:ruin#0@0";

    /// <summary>A world-owned copy of <paramref name="creation"/> (same id, parts, poses and contents), by way of its save JSON.</summary>
    public static Creation Owned(Creation creation, string key = Key)
    {
        var json = JsonNode.Parse(CreationJson.Write(creation))!.AsObject();
        json["world_part"] = key;
        var diagnostics = new List<DataDiagnostic>();
        return CreationJson.Read(json.ToJsonString(), creation.Catalog, "test", diagnostics)
            ?? throw new InvalidOperationException(string.Join("; ", diagnostics.Select(d => d.Message)));
    }
}
