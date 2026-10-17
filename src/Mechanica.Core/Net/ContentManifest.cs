using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;

namespace Mechanica.Core.Net;

/// <summary>A loaded mod's identity as compared on join.</summary>
/// <param name="Id">Mod id.</param>
/// <param name="Version">Mod version string.</param>
public sealed record ModStamp(string Id, string Version);

/// <summary>
/// What content an instance runs: its loaded mods in load order plus a fingerprint of all their data.
/// A client sends its manifest in the join handshake and the host refuses a mismatch, naming the mods
/// that differ so the player knows what to install, remove or update.
/// </summary>
/// <param name="Mods">Loaded mods in load order.</param>
/// <param name="Fingerprint">Hash of every loaded file's content ("" when unknown).</param>
public sealed record ContentManifest(IReadOnlyList<ModStamp> Mods, string Fingerprint)
{
    /// <summary>Most mods a manifest may list on the wire.</summary>
    public const int MaxMods = 512;

    /// <summary>No mods loaded.</summary>
    public static ContentManifest Empty { get; } = new([], "");

    /// <summary>The manifest of loaded content: its mods in load order and the loader's fingerprint.</summary>
    public static ContentManifest From(ModLoadResult content) =>
        new([.. content.Mods.Select(m => new ModStamp(m.Id, m.Manifest.Version ?? ""))], content.Fingerprint);

    /// <summary>The wire form.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, ActionJson.Options);

    /// <summary>Parses the wire form, or returns null when it is malformed or too large.</summary>
    public static ContentManifest? FromJson(string json)
    {
        if (json.Length > 64 * 1024)
            return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<ContentManifest>(json, ActionJson.Options);
            return manifest is null || manifest.Mods.Count > MaxMods || manifest.Mods.Any(m => m is null) ? null : manifest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What a client must change to match the host, one line each; empty when they match. Mods are
    /// named with versions; order and fingerprint are only reported when the mod lists agree.
    /// </summary>
    public static IReadOnlyList<string> Differences(ContentManifest host, ContentManifest client)
    {
        var hostMods = host.Mods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Version);
        var clientMods = client.Mods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Version);
        var differences = new List<string>();
        foreach (var mod in host.Mods)
        {
            if (!clientMods.TryGetValue(mod.Id, out var version))
                differences.Add($"missing {mod.Id} {mod.Version}");
            else if (version != mod.Version)
                differences.Add($"{mod.Id}: host has {mod.Version}, you have {version}");
        }
        foreach (var mod in client.Mods.Where(m => !hostMods.ContainsKey(m.Id)))
            differences.Add($"{mod.Id} {mod.Version} is not on the host");
        if (differences.Count > 0)
            return differences;

        if (!host.Mods.Select(m => m.Id).SequenceEqual(client.Mods.Select(m => m.Id)))
            differences.Add($"load order differs: the host loads {string.Join(", ", host.Mods.Select(m => m.Id))}");
        else if (host.Fingerprint != client.Fingerprint)
            differences.Add("same mods and versions, but their files differ (a modified or outdated copy)");
        return differences;
    }
}
