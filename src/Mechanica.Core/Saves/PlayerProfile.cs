using System.Security.Cryptography;
using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.Saves;

/// <summary>
/// Who a player is across sessions: a random id made once per install and a display name. A client sends
/// its profile in the join handshake, and the host keys saved players (inventory, health, position) by
/// <see cref="Id"/>, so a player gets their things back however their peer id changes.
/// </summary>
/// <param name="Id">32 lowercase hex characters.</param>
/// <param name="Name">Display name, 1 to <see cref="MaxNameLength"/> characters.</param>
public sealed record PlayerProfile(string Id, string Name)
{
    /// <summary>Longest display name.</summary>
    public const int MaxNameLength = 24;

    /// <summary>A new profile with a fresh random id.</summary>
    public static PlayerProfile New(string name) => new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), CleanName(name));

    /// <summary>
    /// A profile whose id is derived from <paramref name="name"/>, so tests that restart instances get the
    /// same player back.
    /// </summary>
    public static PlayerProfile Fixed(string name) =>
        new(Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name)))[..32], CleanName(name));

    /// <summary>True when <paramref name="id"/> has the shape of a profile id.</summary>
    public static bool IsValidId(string? id) => id is { Length: 32 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>
    /// <paramref name="name"/> trimmed, with control characters dropped and cut to <see cref="MaxNameLength"/>;
    /// "Player" when nothing is left.
    /// </summary>
    public static string CleanName(string? name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > MaxNameLength)
            clean = clean[..MaxNameLength].TrimEnd();
        return clean.Length == 0 ? "Player" : clean;
    }

    /// <summary>The profile file's form.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, DataJson.WriteOptions) + "\n";

    /// <summary>Reads a profile file, or returns null when it is malformed or its id is bad.</summary>
    public static PlayerProfile? FromJson(string json)
    {
        try
        {
            var profile = DataJson.Read<PlayerProfile>(json);
            return profile is not null && IsValidId(profile.Id) ? profile with { Name = CleanName(profile.Name) } : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the profile at <paramref name="path"/>, or makes a new one named <paramref name="defaultName"/> and
    /// writes it there when the file is missing or unreadable.
    /// </summary>
    public static PlayerProfile LoadOrCreate(string path, string defaultName)
    {
        if (File.Exists(path) && FromJson(File.ReadAllText(path)) is { } existing)
            return existing;
        var profile = New(defaultName);
        Save(path, profile);
        return profile;
    }

    /// <summary>Writes <paramref name="profile"/> to <paramref name="path"/>, making the folder if needed.</summary>
    public static void Save(string path, PlayerProfile profile)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);
        DataJson.WriteFile(path, profile.ToJson());
    }
}
